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
    string html = await GetHTMLFromUrl($"https://pypistats.org/packages/{package_id.Trim()}");
    var ret = ParseHtml(html, package_id);
    if (ret == null)
    {
        return null;
    }
    cache.Set(package_id, ret, TimeSpan.FromHours(8));
    return ret;
};

async Task<string> GetHTMLFromUrl(string url)
{
    int maxRetries = 3;
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            using var client = new HttpClient();
            return await client.GetStringAsync(url);
        }
        catch (Exception)
        {
            await Task.Delay(1024);
        }
    }
    return String.Empty;
}

DownloadStats? ParseHtml(string html, string package_id)
{
    if (string.IsNullOrWhiteSpace(html))
    {
        return null;
    }
    int downloads_flag = 0;
    int downloads_lastday = 0, downloads_lastweek = 0, downloads_lastmonth = 0;
    foreach (string line in html.Split('\n'))
    {
        if (line == "Downloads last day:")
        {
            downloads_flag = 1;
            continue;
        }
        else if (line == "Downloads last week:")
        {
            downloads_flag = 2;
            continue;
        }
        else if (line == "Downloads last month:")
        {
            downloads_flag = 3;
            continue;
        }
        if (downloads_flag > 0 && int.TryParse(line.Replace(",", String.Empty), out int downloads))
        {
            switch (downloads_flag)
            {
                case 1: downloads_lastday = downloads; break;
                case 2: downloads_lastweek = downloads; break;
                case 3: downloads_lastmonth = downloads; break;
            }
            if (downloads_flag == 3)
            {
                break;
            }
        }
    }
    if (downloads_flag == 0)
    {
        // can not find any downloads amount from the page
        return null;
    }
    return new DownloadStats(package_id, downloads_lastday, downloads_lastweek, downloads_lastmonth);
}

record DownloadStats(string package_id, int downloads_lastday, int downloads_lastweek, int downloads_lastmonth);

#endregion