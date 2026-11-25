# Sample.McpServer

**Acme Orders**: a small MCP server with the docs page and its try-it console turned on. It covers what the
page has to render: recursive and polymorphic schemas, structured output, icons, groups, prompts, resource
templates, and tools gated with `[Authorize]`.

Nothing here is real: every tool returns canned data, and the demo auth accepts whatever it's given.

## Run it

```bash
dotnet run --project samples/Sample.McpServer --launch-profile http
```

| URL | What |
|---|---|
| http://localhost:5168/mcp-docs/ | The docs page (`/` redirects here) |
| http://localhost:5168/mcp | The MCP endpoint (Streamable HTTP, stateless) |
| http://localhost:5168/mcp-docs/catalog.json | The catalog the page is built from; also `catalog.md`, `llms.txt`, `docs.html` |

## What it offers

| Name | Kind | Annotations | Needs |
|---|---|---|---|
| `get_order` | tool | read-only | nothing (try `ORD-404` for a tool error) |
| `search_orders` | tool | read-only | nothing |
| `count_orders` | tool | read-only | nothing |
| `create_order` | tool | modifies data | nothing |
| `cancel_order` | tool | destructive | nothing |
| `get_category_tree` | tool | read-only | nothing |
| `update_prices` | tool | modifies data | nothing |
| `apply_discount` | tool | modifies data | nothing |
| `echo` | tool | read-only | nothing |
| `get_exchange_rate` | tool | read-only | nothing |
| `ping` | tool | read-only | nothing (registered at runtime, no description on purpose) |
| `get_service_status` | tool | read-only | nothing (`[AllowAnonymous]` beats the class's `admin`) |
| `refund_order` | tool | destructive | role `admin` |
| `export_customers` | tool | read-only | role `admin` **and** policy `Finance` (role `finance`) |
| `summarize_order`, `weekly_report` | prompt | | nothing |
| `escalate_order` | prompt | | any signed-in user |
| `catalog_readme` | resource | | nothing |
| `product`, `product_search` | resource template | | nothing |
| `unit_costs` | resource | | policy `Finance` (role `finance`) |

Gated entries are listed for everyone, with a lock badge naming what they need (`GatedPrimitives = Show`, the
default). The MCP endpoint refuses callers who don't have it.

## Signing in

The demo auth reads the roles you want from what you send. Nothing is checked.

### A bearer token (MCP clients and the try-it console)

The token is the comma-separated list of roles it grants:

| Token | Can call |
|---|---|
| none | everything in the table without a "Needs" |
| `user` (any value) | the above, plus `escalate_order` |
| `admin` | the above, plus `refund_order` |
| `admin,finance` | everything, including `export_customers` and `unit_costs` |

**In the docs page:** open a gated tool, press **Try it**, type `admin` into the Authorization field, and Send.
The console adds `Bearer ` for you. The token is kept for the tab (`CredentialStorage = Session`); **Forget**
clears it. Without a token, the same call comes back as "Access forbidden".

**From an MCP client:**

```bash
claude mcp add --transport http acme-orders http://localhost:5168/mcp --header "Authorization: Bearer admin,finance"
```

**With curl**, the shape the console sends (its **Copy as curl** button gives you this for any call):

```bash
curl -N -X POST http://localhost:5168/mcp \
  -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
  -H 'MCP-Protocol-Version: 2026-07-28' -H 'Mcp-Method: tools/call' -H 'Mcp-Name: refund_order' \
  -H 'Authorization: Bearer admin' \
  --data-raw '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"refund_order","arguments":{"orderId":"ORD-1001"},"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientCapabilities":{}}}}'
```

### A cookie (the browser)

Development only:

- http://localhost:5168/demo/sign-in?roles=admin,finance signs the browser in with those roles.
- http://localhost:5168/demo/sign-out signs it out.

The try-it console sends cookies too (`SendCookies`, on by default), so once you're signed in, gated calls work
with the token field empty. A request with `Authorization: Bearer …` uses the token instead of the cookie.

The cookie also decides what the page lists if you switch to per-viewer filtering. Uncomment
`o.GatedPrimitives = McpDocsGatedPrimitives.Hide;` in `Program.cs`: an anonymous browser then sees no gated
entries, and signing in with `admin,finance` brings them back.

### Not for real apps

`DemoBearerHandler` in `Program.cs` accepts any bearer token, in every environment. A real server validates
tokens (JWT bearer, OAuth); the docs page and the console work the same way with it.

## Other things to try

- **Static export:** `dotnet run --project samples/Sample.McpServer -- --export-html site/index.html` writes the
  page as one self-contained file. It has no try-it console: a file can't call the server.
- **Theme:** uncomment the `DefaultColorMode` / `DarkPreset` / `PrimaryColor` line in `Program.cs`.
- **Protect the page:** uncomment `.RequireAuthorization()` after `MapMcpDocs`.
