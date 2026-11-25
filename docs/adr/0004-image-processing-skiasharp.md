# ADR 0004: Avatar image processing with SkiaSharp

- Status: accepted
- Date: 2026-11-25
- Feature: 002 (profile and avatar)

## Context
Users upload profile pictures. The server decodes each upload, so the decoder is an attack surface: malformed files, decompression bombs (a small file that declares a huge canvas), and metadata that leaks location (GPS in EXIF). Every avatar is stored as a 256x256 WebP with the EXIF orientation applied and no metadata. The library must decode JPEG, PNG and WebP, scale and crop, and encode WebP, and it must run in the Linux container image (`mcr.microsoft.com/dotnet/aspnet:10.0`, Debian) on both x64 and arm64.

## Options
- **SkiaSharp** (MIT). A .NET binding for Google Skia, with a native library per platform. Its codec header API (`SKCodec`) reads dimensions without decoding pixels, and it supports scaled JPEG and WebP decode, EXIF orientation, and WebP encoding. The `SkiaSharp.NativeAssets.Linux.NoDependencies` package excludes fontconfig and needs only glibc (libc, libm, libdl, libpthread), which the Debian runtime image already has.
- **ImageSharp** (Six Labors Split License). Pure managed code with a good API, but the license is not MIT-compatible for commercial users of a template. Rejected.
- **Magick.NET** (Apache-2.0). Broad format support, but it bundles a large native ImageMagick with a wide attack surface and a long history of decoder CVEs. Rejected.

## Decision
Use SkiaSharp 4.157.0 with `SkiaSharp.NativeAssets.Linux.NoDependencies` 4.157.0, both pinned in `Directory.Packages.props` and referenced by `AspireBoot.Core`. `AvatarImageProcessor` (sealed, registered as a singleton) runs these steps:

1. **Sniff the magic bytes.** JPEG `FF D8 FF`, PNG `89 50 4E 47 0D 0A 1A 0A`, WebP `RIFF....WEBP`. Anything else returns `avatar.unsupported_type`. The file extension and the Content-Type are ignored.
2. **Check the header before decoding.** `SKCodec.Create` reads the header without decoding pixels. A width or height above 8000, or more than 24 MP, returns `avatar.dimensions_too_large`, which is the decompression-bomb guard. A null codec returns `avatar.corrupt`.
3. **Decode at reduced size.** JPEG and WebP use a scaled decode (`SKCodec.GetScaledDimensions`) at the smallest scale whose short side is still at least 256 px. PNG has no scaled decode, so it is decoded at full size, which the header cap already bounds.
4. **Orient, crop and resize in one pass.** The EXIF origin is applied while drawing, then the image is center-cropped to a square and drawn at 256x256 with `SKSamplingOptions(SKCubicResampler.Mitchell)`.
5. **Encode.** `SKImage.Encode(SKEncodedImageFormat.Webp, 80)`. Re-encoding from pixels writes no EXIF, XMP or GPS data. The tests prove this with a fixture JPEG that carries GPS EXIF.

Concurrency is bounded by `AvatarProcessingLimiter`, a process-wide `SemaphoreSlim`. Its size is `Avatars:MaxConcurrentProcessing` (default 2, validated 1 to 64). A request that waits longer than `Avatars:ProcessingWaitSeconds` (default 10, validated 1 to 60) gets `503 avatar.busy`.

Failure codes are static `Failure` values in `AvatarImageFailures`, so the endpoint can translate them into Problem Details without exposing decoder messages.

### Native dependencies and verification
- The NoDependencies package needs only glibc. `aspnet:10.0` covers that, so the Dockerfiles need no `apt` step.
- arm64 is verified locally with Podman on Apple Silicon (T16). `scripts/verify-images.sh` builds the Api and Worker images, runs `ldd` on `libSkiaSharp.so` in the runtime image, and runs the processor tests in an `mcr.microsoft.com/dotnet/sdk:10.0` Linux container.
- x64 is verified by the CI `images` job, which builds the same images.

## Consequences
- **Memory bound and sizing.** Each concurrent decode holds at most one 24 MP bitmap at 4 bytes per pixel, so one slot costs about 96 MB at the cap. The peak is `MaxConcurrentProcessing` x 96 MB, plus up to 5 MB of input per slot and the encoder working memory. The default of 2 gives a peak of about 192 MB, which is the headroom the API host needs for avatar processing on top of its normal working set. Each extra slot adds about 100 MB of peak memory, so raise the value only on hosts with that much free memory. PNG is the only format that decodes at full size, so the 24 MP cap is what makes the slot bound hold.
- **Native code in the process.** Skia is a large C++ library, so security updates come through new SkiaSharp releases. Pin the version, and review it when bumping. Renovate or an equivalent update tool should track it.
- **Lossy output.** Avatars are re-encoded as WebP at quality 80, so pixels are not byte-identical to the upload. That is intended: it strips metadata and bounds size.
- **Operational cost.** Images are about 10 to 30 KB each, so the API serves them with immutable private caching (see the storage design in feature 002).
- **Rejected alternatives stay rejected.** Changing to ImageSharp would require a commercial license review. Magick.NET would require a larger container and a wider attack review.

## Sources (verified 2026-11-25)
- SKCodec.GetScaledDimensions: https://learn.microsoft.com/dotnet/api/skiasharp.skcodec.getscaleddimensions
- SkiaSharp NoDependencies (MIT, fontconfig excluded, glibc-only deps): https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux.NoDependencies
- SkiaSharp license: https://github.com/mono/SkiaSharp/blob/main/LICENSE.md
- ImageSharp Six Labors Split License: https://github.com/SixLabors/ImageSharp/blob/main/LICENSE
- SKCodec header-only info and EncodedOrigin: https://learn.microsoft.com/dotnet/api/skiasharp.skcodec
- .NET container images (aspnet:10.0 Debian base): https://learn.microsoft.com/dotnet/core/docker/container-images
