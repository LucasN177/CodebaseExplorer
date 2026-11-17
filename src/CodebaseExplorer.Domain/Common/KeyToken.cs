namespace CodebaseExplorer.Domain.Common;

/// <summary>Validation of kind tokens used in natural keys and identifiers: <c>[a-z0-9.+-]+</c>.</summary>
internal static class KeyToken
{
    public static string Validate(string token, string parameterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(token, parameterName);

        foreach (var character in token)
        {
            if (character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '+' or '-'))
            {
                throw new ArgumentException($"'{token}' is not a valid token; expected [a-z0-9.+-]+.", parameterName);
            }
        }

        return token;
    }
}
