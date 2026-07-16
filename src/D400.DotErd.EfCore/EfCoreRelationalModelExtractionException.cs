namespace D400.DotErd.EfCore;

/// <summary>
/// Represents a safe, actionable EF Core relational model extraction failure.
/// </summary>
public sealed class EfCoreRelationalModelExtractionException : Exception
{
    /// <summary>
    /// Initializes a new extraction exception.
    /// </summary>
    /// <param name="message">The safe diagnostic message.</param>
    /// <param name="innerException">The underlying exception.</param>
    public EfCoreRelationalModelExtractionException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

