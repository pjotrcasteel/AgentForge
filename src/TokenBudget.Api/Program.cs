using System.Text.Json.Serialization;
using TokenBudget.Agent;
using TokenBudget.Api.Endpoints;
using TokenBudget.Api.Security;
using TokenBudget.Application;
using TokenBudget.Billing;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(server =>
    server.Limits.MaxRequestBodySize = TokenBudgetSecurityMiddleware.MaxRequestBytes);
builder.Services.AddSingleton(AnalyzePlanService.CreateDefault());
builder.Services.AddSingleton<BudgetPolicyEvaluator>();
builder.Services.AddSingleton<PromptScopeAdvisor>();
builder.Services.AddSingleton<IAgentPlanReviewer, MicrosoftAgentFrameworkPlanReviewer>();
builder.Services.AddSingleton<AgentPreflightService>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

WebApplication app = builder.Build();
app.UseTokenBudgetSecurity();
app.MapTokenBudgetEndpoints();
app.Run();
