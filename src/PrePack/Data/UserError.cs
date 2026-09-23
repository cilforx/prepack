namespace PrePack.Data;

/// <summary>An error whose Thai message is safe and useful to show the user as-is.</summary>
public sealed class UserError(string message) : Exception(message);
