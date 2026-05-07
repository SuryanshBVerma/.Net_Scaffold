// GlobalUsings.cs — SharedKernel
// ──────────────────────────────
// These using directives are injected into every file in this project.
// ImplicitUsings=enable (Directory.Build.props) already pulls in System.*,
// Microsoft.Extensions.*. We add domain/framework-specific ones here.
//
// LEARNING: Keep GlobalUsings lean. Only add namespaces that truly appear
// in most files. One-off usings belong in the file that needs them.

global using Microsoft.AspNetCore.Builder;
global using Microsoft.AspNetCore.Http;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using System.Security.Claims;
