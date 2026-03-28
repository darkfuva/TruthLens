using TruthLens.Api.Endpoints;
using TruthLens.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    // Dev dashboard runs on Vite default port.
    options.AddPolicy("DashboardDev", policy =>
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("DashboardDev");
}

// In development we keep HTTP to simplify local React proxy setup.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapEventsEndpoints();

app.Run();
