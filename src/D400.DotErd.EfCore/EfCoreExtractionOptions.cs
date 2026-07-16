using Microsoft.EntityFrameworkCore;

namespace D400.DotErd.EfCore;

/// <summary>
/// Configures extraction of a normalized schema model from an EF Core <see cref="DbContext" />.
/// </summary>
/// <param name="DatabaseName">The database name to place in the normalized model. When omitted, the extractor uses metadata available from the context without opening a connection.</param>
/// <param name="ExcludeMigrationsHistory">A value indicating whether the SQL Server migrations history table is excluded.</param>
public sealed record EfCoreExtractionOptions(string? DatabaseName = null, bool ExcludeMigrationsHistory = true);

