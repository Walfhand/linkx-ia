using LinkxAi.Api.Modules.Turns.Infrastructure.Security;
using QuickApi.Engine.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMinimalEndpoints(options => options.SetBaseApiPath("api/v1"));

var app = builder.Build();
app.UseMiddleware<LinkxSignatureMiddleware>();
app.UseMinimalEndpoints();
app.Run();

public partial class Program;
