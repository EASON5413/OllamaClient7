﻿using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// 啟用 CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddControllers();
builder.Services.AddSingleton<HttpClient>();

var app = builder.Build();
app.UseCors();
app.MapControllers();
app.Run();

public class SummaryController : ControllerBase
{
    private static readonly HttpClient client = new HttpClient
    {
        Timeout = TimeSpan.FromMinutes(10) // 增加超時時間到 10 分鐘
    };

    // 模型 API 的路徑
    private const string API_ENDPOINT = "http://localhost:11434/api/generate";

    [HttpPost("generate-summary")]
    public async Task<IActionResult> GenerateSummary([FromBody] SummaryRequest request)
    {
        Console.WriteLine($"接收到的 module: {request.Module}");
        Console.WriteLine($"接收到的 content: {request.Content}");

        if (string.IsNullOrEmpty(request.Module) || string.IsNullOrEmpty(request.Content))
        {
            return BadRequest(new { error = "Invalid input", message = "請提供有效的 module 和 content。" });
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        string summary = await GenerateTextAsync(request.Content, request.Module);
        stopwatch.Stop();

        if (summary == null)
        {
            return StatusCode(500, "模型生成失敗。");
        }

        return Ok(new
        {
            module = request.Module,
            time = $"{stopwatch.ElapsedMilliseconds}ms",
            response = summary
        });
    }

    private static async Task<string> GenerateTextAsync(string prompt, string model)
    {
        try
        {
            // 在這裡加入提示字
            string fullPrompt = $"摘要以下文句內容:\n{prompt}";

            var requestBody = new { model, prompt = fullPrompt };
            var json = JsonSerializer.Serialize(requestBody);
            Console.WriteLine($"發送給模型的請求：{json}");

            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            // 發送請求到模型 API
            using (var response = await client.PostAsync(API_ENDPOINT, content))
            {
                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"模型 API 請求失敗，狀態碼：{response.StatusCode}，內容：{errorContent}");
                    return null;
                }

                string fullResponse = "";
                using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync()))
                {
                    while (!reader.EndOfStream)
                    {
                        string line = await reader.ReadLineAsync();
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        try
                        {
                            using (JsonDocument jsonDocument = JsonDocument.Parse(line))
                            {
                                if (jsonDocument.RootElement.TryGetProperty("response", out var responseElement))
                                {
                                    fullResponse += responseElement.GetString();
                                }
                            }
                        }
                        catch (JsonException ex)
                        {
                            Console.WriteLine($"JSON 解析錯誤：{ex.Message}，行內容：{line}");
                        }
                    }
                }

                Console.WriteLine($"完整生成的回應：{fullResponse}");
                return fullResponse;
            }
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"HTTP 請求錯誤: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"發生未知異常: {ex.Message}");
            return null;
        }
    }
}

public class SummaryRequest
{
    public string Module { get; set; } // 模型名稱
    public string Content { get; set; } // HTML 或純文字內容
}
