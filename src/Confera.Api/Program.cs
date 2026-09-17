using Confera.Api.Errors;
using Confera.Application.Availability;
using Confera.Application.Bookings;
using Confera.Application.Reports;
using Confera.Application.Rooms;
using Confera.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddConferaPersistence(builder.Configuration);
builder.Services.AddConferaDatabaseHealth();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CreateBookingService>();
builder.Services.AddScoped<RoomManagementService>();
builder.Services.AddScoped<SearchAvailabilityService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
    options.InvalidModelStateResponseFactory = ApiProblems.InvalidRequest);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Confera API v1"));
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
