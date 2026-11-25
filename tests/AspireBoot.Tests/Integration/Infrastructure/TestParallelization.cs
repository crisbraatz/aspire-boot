using Xunit.v3;

// The integration tests share one PostgreSQL database (Respawn resets it) and one Mailpit inbox.
// Running test classes in parallel makes resets deadlock and clears mail another test is waiting for.
[assembly: ParallelizationAttribute(MaxThreads = 1)]