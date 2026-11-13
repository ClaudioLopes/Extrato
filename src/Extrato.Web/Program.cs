using Extrato.Web;
using Extrato.Web.Servicos;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<SessaoEstado>();
builder.Services.AddScoped<RedeEstado>();
builder.Services.AddScoped<ArmazenamentoLocal>();
builder.Services.AddScoped<ApiClient>();

await builder.Build().RunAsync();
