namespace D400.DotErd.Core;

/// <summary>
/// Provides validation for normalized database schema models.
/// </summary>
public static class SchemaModelValidator
{
    /// <summary>
    /// Validates duplicate tables, duplicate columns, and invalid foreign key references.
    /// </summary>
    /// <param name="model">The database model to validate.</param>
    /// <returns>A validation result containing any discovered errors.</returns>
    public static SchemaValidationResult Validate(DatabaseModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var errors = new List<SchemaValidationError>();
        var tablesByIdentity = new Dictionary<LogicalIdentity, DatabaseTable>();
        var columnsByIdentity = new Dictionary<LogicalIdentity, DatabaseColumn>();

        foreach (var schema in model.Schemas)
        {
            var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var table in schema.Tables)
            {
                if (!tableNames.Add(table.Name))
                {
                    errors.Add(new SchemaValidationError(
                        table.Identity,
                        SchemaValidationErrorCodes.DuplicateTable,
                        $"Duplicate table '{schema.Name}.{table.Name}' exists in database '{model.Name}'."));
                }

                if (!tablesByIdentity.TryAdd(table.Identity, table))
                {
                    errors.Add(new SchemaValidationError(
                        table.Identity,
                        SchemaValidationErrorCodes.DuplicateTable,
                        $"Duplicate table identity '{table.Identity}' exists in database '{model.Name}'."));
                }

                var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var column in table.Columns)
                {
                    if (!columnNames.Add(column.Name))
                    {
                        errors.Add(new SchemaValidationError(
                            column.Identity,
                            SchemaValidationErrorCodes.DuplicateColumn,
                            $"Duplicate column '{schema.Name}.{table.Name}.{column.Name}' exists."));
                    }

                    columnsByIdentity.TryAdd(column.Identity, column);
                }
            }
        }

        foreach (var table in tablesByIdentity.Values)
        {
            foreach (var foreignKey in table.ForeignKeys)
            {
                ValidateForeignKey(foreignKey, tablesByIdentity, columnsByIdentity, errors);
            }
        }

        return new SchemaValidationResult(errors);
    }

    private static void ValidateForeignKey(
        ForeignKeyDefinition foreignKey,
        IReadOnlyDictionary<LogicalIdentity, DatabaseTable> tablesByIdentity,
        IReadOnlyDictionary<LogicalIdentity, DatabaseColumn> columnsByIdentity,
        ICollection<SchemaValidationError> errors)
    {
        if (!tablesByIdentity.ContainsKey(foreignKey.DependentTableIdentity))
        {
            errors.Add(new SchemaValidationError(
                foreignKey.Identity,
                SchemaValidationErrorCodes.InvalidForeignKeyReference,
                $"Foreign key '{foreignKey.Name}' references missing dependent table '{foreignKey.DependentTableIdentity}'."));
        }

        if (!tablesByIdentity.ContainsKey(foreignKey.PrincipalTableIdentity))
        {
            errors.Add(new SchemaValidationError(
                foreignKey.Identity,
                SchemaValidationErrorCodes.InvalidForeignKeyReference,
                $"Foreign key '{foreignKey.Name}' references missing principal table '{foreignKey.PrincipalTableIdentity}'."));
        }

        foreach (var mapping in foreignKey.ColumnMappings)
        {
            if (!columnsByIdentity.ContainsKey(mapping.DependentColumnIdentity))
            {
                errors.Add(new SchemaValidationError(
                    foreignKey.Identity,
                    SchemaValidationErrorCodes.InvalidForeignKeyReference,
                    $"Foreign key '{foreignKey.Name}' references missing dependent column '{mapping.DependentColumnIdentity}'."));
            }

            if (!columnsByIdentity.ContainsKey(mapping.PrincipalColumnIdentity))
            {
                errors.Add(new SchemaValidationError(
                    foreignKey.Identity,
                    SchemaValidationErrorCodes.InvalidForeignKeyReference,
                    $"Foreign key '{foreignKey.Name}' references missing principal column '{mapping.PrincipalColumnIdentity}'."));
            }
        }
    }
}

/// <summary>
/// Provides stable validation error codes for normalized schema models.
/// </summary>
public static class SchemaValidationErrorCodes
{
    /// <summary>
    /// Indicates that two or more tables share the same schema-qualified identity.
    /// </summary>
    public const string DuplicateTable = "duplicate_table";

    /// <summary>
    /// Indicates that two or more columns share the same table-qualified identity.
    /// </summary>
    public const string DuplicateColumn = "duplicate_column";

    /// <summary>
    /// Indicates that a foreign key references a missing table or column.
    /// </summary>
    public const string InvalidForeignKeyReference = "invalid_foreign_key_reference";
}

/// <summary>
/// Represents the result of validating a normalized schema model.
/// </summary>
public sealed record SchemaValidationResult
{
    /// <summary>
    /// Initializes a new schema validation result.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    public SchemaValidationResult(IEnumerable<SchemaValidationError> errors)
    {
        Errors = new ValueList<SchemaValidationError>(errors);
    }

    /// <summary>
    /// Gets a value indicating whether validation completed without errors.
    /// </summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>
    /// Gets the validation errors.
    /// </summary>
    public ValueList<SchemaValidationError> Errors { get; }
}

/// <summary>
/// Represents one validation error in a normalized schema model.
/// </summary>
/// <param name="ObjectIdentity">The identity of the object that caused the error, when available.</param>
/// <param name="Code">The stable validation error code.</param>
/// <param name="Message">The human-readable validation error message.</param>
public sealed record SchemaValidationError(LogicalIdentity? ObjectIdentity, string Code, string Message);

