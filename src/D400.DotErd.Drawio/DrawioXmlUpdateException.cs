namespace D400.DotErd.Drawio;

/// <summary>
/// Represents a safe failure while reading or updating an existing draw.io document.
/// </summary>
public sealed class DrawioXmlUpdateException : Exception
{
    /// <summary>
    /// Initializes a new draw.io update exception.
    /// </summary>
    /// <param name="message">The safe, actionable failure message.</param>
    /// <param name="innerException">The underlying XML or update exception.</param>
    public DrawioXmlUpdateException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
