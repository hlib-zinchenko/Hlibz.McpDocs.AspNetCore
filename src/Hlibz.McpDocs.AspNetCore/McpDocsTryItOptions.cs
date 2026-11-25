using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ModelContextProtocol.AspNetCore;

namespace Hlibz.McpDocs.AspNetCore;

/// <summary>
/// Settings for the try-it console, turned on with <see cref="McpDocsOptions.EnableTryIt"/>.
/// </summary>
/// <remarks>
/// <para>
/// The console adds a "Try it" button to each tool, prompt and resource. It opens a panel where a viewer edits the
/// arguments and a token, sends one request to the MCP endpoint from their browser, and reads the result: a tool's
/// content blocks and <c>structuredContent</c>, the messages a prompt expands to, a resource's contents.
/// </para>
/// <para>
/// It speaks stateless Streamable HTTP (protocol <c>2026-07-28</c>: no <c>initialize</c>, no session), the
/// SDK's default. It isn't offered when the server is <see cref="HttpServerSessionMode.Stateful"/>, on a static
/// export, or when the MCP endpoint is on another origin, which the page's <c>connect-src 'self'</c> and the
/// endpoint's lack of CORS both rule out.
/// </para>
/// </remarks>
public sealed class McpDocsTryItOptions
{
    private TimeSpan? _credentialLifetime = TimeSpan.FromHours(8);

    /// <summary>
    /// Where the token a viewer types is kept. Defaults to <see cref="McpDocsTryItCredentialStorage.Memory"/>: it
    /// is gone on a reload. The page's Content-Security-Policy allows no inline or third-party script, which is
    /// what keeps an injected one from reading a stored token.
    /// </summary>
    public McpDocsTryItCredentialStorage CredentialStorage { get; set; }

    /// <summary>
    /// How long a stored token is kept after it was last typed, or <see langword="null"/> to keep it until the
    /// viewer presses "Forget" or clears site data. Defaults to 8 hours. Only matters when
    /// <see cref="CredentialStorage"/> stores it; checked whenever the console opens.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public TimeSpan? CredentialLifetime
    {
        get => _credentialLifetime;
        set
        {
            if (value is { } lifetime && lifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), lifetime,
                    "CredentialLifetime must be positive, or null to keep the token until it is forgotten.");
            }

            _credentialLifetime = value;
        }
    }

    /// <summary>
    /// Whether requests carry the viewer's cookies for this origin. Defaults to <see langword="true"/>, so an MCP
    /// endpoint using cookie authentication works for a viewer who is already signed in. Set it to
    /// <see langword="false"/> to send only the token typed into the console.
    /// </summary>
    public bool SendCookies { get; set; } = true;

    /// <summary>
    /// Whether only tools annotated read-only (<c>readOnlyHint: true</c>) get a "Try it" button. Prompts and
    /// resources are reads and always get one. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// For docs in front of a real system, where a viewer pressing Send with their own cookies or token would make
    /// a real change. A convenience, <b>not</b> access control: the server still accepts whatever any other MCP
    /// client sends it.
    /// </remarks>
    public bool ReadOnlyOnly { get; set; }

    /// <summary>Why the console can't run against this server, or <see langword="null"/> when it can.</summary>
    internal static string? Unavailable(IServiceProvider services) =>
        services.GetService<IOptions<HttpServerTransportOptions>>()?.Value.SessionMode == HttpServerSessionMode.Stateful
            ? "The MCP server is stateful (HttpServerTransportOptions.SessionMode = Stateful); the console only calls stateless servers."
            : null;
}

/// <summary>Where the try-it console keeps the token a viewer types, set through <see cref="McpDocsTryItOptions.CredentialStorage"/>.</summary>
/// <remarks>
/// A stored token is kept per MCP endpoint, so two docs pages for the same server share it. Anything a script on
/// this origin can run can read browser storage, so a stored token is only as safe as the origin it lives on.
/// </remarks>
public enum McpDocsTryItCredentialStorage
{
    /// <summary>Kept in the page's memory only: gone on a reload. The default.</summary>
    Memory,

    /// <summary>Kept in <c>sessionStorage</c>: survives reloads, gone when the tab is closed.</summary>
    Session,

    /// <summary>
    /// Kept in <c>localStorage</c>: survives reloads, new tabs and browser restarts, until
    /// <see cref="McpDocsTryItOptions.CredentialLifetime"/> runs out or the viewer presses "Forget".
    /// </summary>
    Local,
}
