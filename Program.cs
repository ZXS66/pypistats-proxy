using Microsoft.Extensions.Caching.Memory;
using MemoryCache cache = new(new MemoryCacheOptions());

const string _PYPI_HOST = "https://pypi.org";

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(_PYPI_HOST).AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
#if !DEBUG
// no redirect to https, because we are using a nginx proxy
// app.UseHttpsRedirection();
builder.WebHost.UseUrls("http://+:8080");
#endif

app.MapPost("/package/{package_id}", GetDownloadStats).WithName(nameof(GetDownloadStats)).WithOpenApi();
app.UseCors();

app.Run();

#region main logic

async Task<DownloadStats?> GetDownloadStats(string package_id, [Microsoft.AspNetCore.Mvc.FromHeader(Name = "referer")] string referrerHeader = "")
{
    if (String.IsNullOrWhiteSpace(package_id))
    {
        return null;
    }
#if !DEBUG
    if (String.IsNullOrWhiteSpace(referrerHeader) || !referrerHeader.StartsWith(_PYPI_HOST))
    {
        return null;
    }
#endif
    if (cache.TryGetValue(package_id, out DownloadStats? cachedValue))
    {
        return cachedValue;
    }
    var ret = await queryPyPIStatsAPI(package_id);
    if (ret == null)
    {
        return null;
    }
    cache.Set(package_id, ret, TimeSpan.FromHours(8));
    return ret;
};

async Task<DownloadStats?> queryPyPIStatsAPI(string package_id)
{
    int maxRetries = 3;
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            using var client = new HttpClient();
            var result = await client.GetFromJsonAsync<PypistatsResult>($"https://pypistats.org/api/packages/{package_id.Trim()}/recent");
            if (result == null || String.IsNullOrWhiteSpace(result.package))
            {
                throw new Exception("invalid result from pypistats API");
            }
            return new DownloadStats(package_id, result.data.last_day, result.data.last_week, result.data.last_month);
        }
        catch (Exception)
        {
            await Task.Delay(1024);
        }
    }
    return null;
}
/// <summary>
/// data structure represents the download stats of a python package
/// </summary>
/// <param name="package_id"></param>
/// <param name="downloads_lastday"></param>
/// <param name="downloads_lastweek"></param>
/// <param name="downloads_lastmonth"></param>
record DownloadStats(string package_id, int downloads_lastday, int downloads_lastweek, int downloads_lastmonth);

record PypistatsResult(PypistatsResultData data, string package, string type);
record PypistatsResultData(int last_day, int last_week, int last_month);

#endregion