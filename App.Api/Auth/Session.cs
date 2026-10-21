namespace App.Api;

public sealed record Session(string AccessToken, string TokenType, int ExpiresIn, SessionEmployee Employee);
