using System.Buffers;
using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AspireBoot.Core.Persistence.Auditing;

/// <summary>
/// Pure translation of tracked entries into <see cref="AuditEntry"/> rows. Each row serializes its changes once with a
/// <see cref="Utf8JsonWriter"/>. Owned entries are folded into the owner row; excluded owned types record only that they changed.
/// </summary>
public static class AuditEntryFactory
{
    public static IReadOnlyList<AuditEntry> Create(
        IEnumerable<EntityEntry> entries,
        Guid? actorUserId,
        DateTimeOffset occurredAt,
        string? traceId)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var result = new List<AuditEntry>();
        foreach (var entry in entries)
        {
            var row = CreateRow(entry, actorUserId, occurredAt, traceId);
            if (row is not null)
            {
                result.Add(row);
            }
        }

        return result;
    }

    private static AuditEntry? CreateRow(EntityEntry entry, Guid? actorUserId, DateTimeOffset occurredAt, string? traceId)
    {
        var metadata = entry.Metadata;
        if (metadata.IsOwned() || !AuditPolicy.IsAudited(metadata))
        {
            return null;
        }

        var ownedChanges = entry.References
            .Where(reference => reference.TargetEntry is { } target && target.Metadata.IsOwned() && IsChanged(target.State))
            .Select(reference => (Name: reference.Metadata.Name, Target: reference.TargetEntry!))
            .ToArray();

        var action = entry.State switch
        {
            EntityState.Added => AuditEntry.ActionInsert,
            EntityState.Deleted => AuditEntry.ActionDelete,
            EntityState.Modified => AuditEntry.ActionUpdate,
            _ => ownedChanges.Length > 0 ? AuditEntry.ActionUpdate : null,
        };

        if (action is null)
        {
            return null;
        }

        var buffer = new ArrayBufferWriter<byte>();
        var hasChanges = false;
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            hasChanges |= WriteProperties(writer, entry, action, prefix: null);

            foreach (var (name, target) in ownedChanges)
            {
                if (AuditPolicy.IsExcludedOwned(target.Metadata))
                {
                    writer.WriteStartObject(JsonName(name));
                    writer.WriteBoolean("changed", true);
                    writer.WriteEndObject();
                    hasChanges = true;
                }
                else
                {
                    hasChanges |= WriteProperties(writer, target, action, prefix: name);
                }
            }

            writer.WriteEndObject();
        }

        if (!hasChanges)
        {
            return null;
        }

        return AuditEntry.Create(
            occurredAt,
            actorUserId,
            metadata.GetTableName() ?? metadata.GetDefaultTableName() ?? metadata.ShortName(),
            metadata.ClrType.Name,
            BuildKey(entry),
            action,
            System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan),
            traceId);
    }

    private static bool IsChanged(EntityState state) => state is EntityState.Added or EntityState.Modified or EntityState.Deleted;

    private static string JsonName(string name) => name.Length == 0 ? name : string.Create(CultureInfo.InvariantCulture, $"{char.ToLowerInvariant(name[0])}{name[1..]}");

    private static bool WriteProperties(Utf8JsonWriter writer, EntityEntry entry, string action, string? prefix)
    {
        var written = false;
        var clrType = entry.Metadata.ClrType;
        var isOwned = entry.Metadata.IsOwned();

        foreach (var property in entry.Properties)
        {
            var metadata = property.Metadata;
            if (isOwned && (metadata.IsKey() || metadata.IsForeignKey()))
            {
                continue;
            }

            if (action == AuditEntry.ActionUpdate)
            {
                if (!property.IsModified || ValuesEqual(property.OriginalValue, property.CurrentValue))
                {
                    continue;
                }
            }

            var masked = AuditPolicy.IsMasked(clrType, metadata.Name);
            writer.WriteStartObject(prefix is null ? metadata.Name : $"{prefix}.{metadata.Name}");
            if (action != AuditEntry.ActionInsert)
            {
                writer.WritePropertyName("old");
                WriteValue(writer, metadata, property.OriginalValue, masked);
            }

            if (action != AuditEntry.ActionDelete)
            {
                writer.WritePropertyName("new");
                WriteValue(writer, metadata, property.CurrentValue, masked);
            }

            writer.WriteEndObject();
            written = true;
        }

        return written;
    }

    private static string BuildKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey()!;
        var clrType = entry.Metadata.ClrType;

        if (key.Properties.Count == 1)
        {
            var property = entry.Property(key.Properties[0].Name);
            EnsureKnown(entry, property);
            return KeyText(property.CurrentValue, AuditPolicy.IsMasked(clrType, property.Metadata.Name));
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var keyProperty in key.Properties)
            {
                var property = entry.Property(keyProperty.Name);
                EnsureKnown(entry, property);
                writer.WriteString(keyProperty.Name, KeyText(property.CurrentValue, AuditPolicy.IsMasked(clrType, keyProperty.Name)));
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void EnsureKnown(EntityEntry entry, PropertyEntry property)
    {
        if (property.IsTemporary)
        {
            throw new InvalidOperationException(
                $"Audited entity {entry.Metadata.ShortName()} has a temporary key value for {property.Metadata.Name}. Audited entities must use client-generated keys.");
        }
    }

    private static string KeyText(object? value, bool masked) => masked
        ? AuditPolicy.MaskedValue
        : value switch
        {
            null => string.Empty,
            byte[] bytes => Convert.ToBase64String(bytes),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

    private static bool ValuesEqual(object? left, object? right) => left is byte[] a && right is byte[] b
        ? a.AsSpan().SequenceEqual(b)
        : Equals(left, right);

    private static void WriteValue(Utf8JsonWriter writer, IProperty property, object? value, bool masked)
    {
        if (masked)
        {
            writer.WriteStringValue(AuditPolicy.MaskedValue);
            return;
        }

        if (value is not null)
        {
            var converter = property.GetTypeMapping().Converter;
            if (converter is not null)
            {
                value = converter.ConvertToProvider(value);
            }
        }

        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string text:
                writer.WriteStringValue(text);
                break;
            case bool flag:
                writer.WriteBooleanValue(flag);
                break;
            case int number:
                writer.WriteNumberValue(number);
                break;
            case long number:
                writer.WriteNumberValue(number);
                break;
            case uint number:
                writer.WriteNumberValue(number);
                break;
            case Guid id:
                writer.WriteStringValue(id);
                break;
            case DateTimeOffset moment:
                writer.WriteStringValue(moment);
                break;
            case DateTime moment:
                writer.WriteStringValue(moment);
                break;
            case byte[] bytes:
                writer.WriteBase64StringValue(bytes);
                break;
            default:
                JsonSerializer.Serialize(writer, value, value.GetType());
                break;
        }
    }
}