using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcClient;
using GrpcClient.Records;
using System.Diagnostics;
using System.Net.Http.Json;

Console.OutputEncoding = System.Text.Encoding.UTF8;



var cts = new CancellationTokenSource();
Console.WriteLine("🔮 Magic is happening... please wait!");
var loaderTask = ShowLoader("Running benchmarks", cts.Token);

// Run  gRPC + REST tests here
await RunBenchmarks();

cts.Cancel(); // stop loader
Console.ReadLine();


#region SIMPLE GRPC CALL
/*
Console.WriteLine("press any key ...");
Console.ReadLine();
using var channel = GrpcChannel.ForAddress("https://localhost:7110");
var client = new RunChart.RunChartClient(channel);


//1. Simple RPC
Console.WriteLine("Simple RPC");

var snapshot = await client.GetSnapshotAsync(new SnapshotRequest { PartNumber = "PN-12345" });
Console.WriteLine($"Snapshot: {snapshot.Measurement}");
Console.ReadLine();


//2. Client-streaming RPC
Console.WriteLine("Client-streaming RPC");

using var call = client.SendMeasurements();
for (int i = 0; i < 5; i++)
{
await call.RequestStream.WriteAsync(new DataPoint
{
Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
PartNumber = "PN-12345",
SpecNominal = 10.0,
SpecTolerance = 0.5,
Measurement = 9.5 + i * 0.1
});
}
await call.RequestStream.CompleteAsync();
var summary = await call.ResponseAsync;
Console.WriteLine($"Summary: Count={summary.Count}, Avg={summary.Average}");
Console.ReadLine();

//3. Server-streaming RPC
Console.WriteLine("Server-streaming RPC");

using var monitorCall = client.Monitor(new SnapshotRequest { PartNumber = "PN-12345" });
await foreach (var dp in monitorCall.ResponseStream.ReadAllAsync())
{
Console.WriteLine($"Monitor: {dp.Measurement}");
}
Console.ReadLine();

//4. Bi-directional streaming RPC
Console.WriteLine("Bi-directional streaming RPC");

using var bidiCall = client.SendAndMonitor();

var sendTask = Task.Run(async () =>
{
for (int i = 0; i < 5; i++)
{
await bidiCall.RequestStream.WriteAsync(new DataPoint
{
    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
    PartNumber = "PN-12345",
    SpecNominal = 10.0,
    SpecTolerance = 0.5,
    Measurement = 9.5 + i * 0.1
});
await Task.Delay(500);
}
await bidiCall.RequestStream.CompleteAsync();
});

await foreach (var dp in bidiCall.ResponseStream.ReadAllAsync())
{
Console.WriteLine($"Server feedback: {dp.Measurement}");
}
Console.ReadLine();

*/
#endregion



static async Task ShowLoader(string message, CancellationToken token)
{
    var spinner = new[] { "|", "/", "-", "\\" };
    int counter = 0;

    while (!token.IsCancellationRequested)
    {
        Console.Write($"\r{message} {spinner[counter]}");
        counter = (counter + 1) % spinner.Length;
        await Task.Delay(100);
        if (token.IsCancellationRequested) break;

    }

    Console.WriteLine(); // move to next line after done
    
}

static async Task RunBenchmarks()
{
    using var channel = GrpcChannel.ForAddress("https://localhost:7110");
    var grpcClient = new RunChart.RunChartClient(channel);
    var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5110") };

    var results = new List<(string Method, long GrpcTime, long RestTime, string Output)>();
    var sw = new Stopwatch();

    // Warm-up gRPC channel
    await grpcClient.GetSnapshotAsync(new SnapshotRequest { PartNumber = "Warmup" });

    // 1. GetSnapshot
    sw.Restart();
    var grpcSnap = await grpcClient.GetSnapshotAsync(new SnapshotRequest { PartNumber = "PN-12345" });
    sw.Stop();
    long grpcSnapTime = sw.ElapsedMilliseconds;

    sw.Restart();
    var restSnap = await httpClient.GetFromJsonAsync<DataPointDto>("/snapshot/PN-12345");
    sw.Stop();
    long restSnapTime = sw.ElapsedMilliseconds;

    results.Add(("GetSnapshot", grpcSnapTime, restSnapTime, $"Meas={grpcSnap.Measurement}"));

    // 2. SendMeasurements
    using var call = grpcClient.SendMeasurements();
    for (int i = 0; i < 5; i++)
    {
        await call.RequestStream.WriteAsync(new DataPoint
        {
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            PartNumber = $"PN-{i}",
            SpecNominal = 10.0,
            SpecTolerance = 0.5,
            Measurement = 9.5 + i * 0.1
        });
    }
    await call.RequestStream.CompleteAsync();
    sw.Restart();
    var grpcSummary = await call.ResponseAsync;
    sw.Stop();
    long grpcSummaryTime = sw.ElapsedMilliseconds;

    sw.Restart();
    var restSummaryResp = await httpClient.PostAsJsonAsync("/measurements", new[]
    {
            new DataPointDto(DateTime.UtcNow,"PN-1",10,0.5,9.7),
            new DataPointDto(DateTime.UtcNow,"PN-2",10,0.5,9.8),
            new DataPointDto(DateTime.UtcNow,"PN-3",10,0.5,9.9)
        });
    var restSummary = await restSummaryResp.Content.ReadFromJsonAsync<SummaryDto>();
    sw.Stop();
    long restSummaryTime = sw.ElapsedMilliseconds;

    results.Add(("SendMeasurements", grpcSummaryTime, restSummaryTime, $"Count={grpcSummary.Count}, Avg={grpcSummary.Average}"));

    // 3. Monitor (server-streaming)

    // gRPC total throughput
    sw = Stopwatch.StartNew();
    int grpcCount = 0;
    using var monitorCall = grpcClient.Monitor(new SnapshotRequest { PartNumber = "PN-12345" });
    await foreach (var dp in monitorCall.ResponseStream.ReadAllAsync())
    {
        grpcCount++;
    }
    sw.Stop();
    var grpcTotalMs = sw.ElapsedMilliseconds;

    // REST total throughput
    sw.Restart();
    int restCount = 0;
    var streamMonitor = await httpClient.GetStreamAsync("/monitor/PN-12345");
    using var readerMonitor = new StreamReader(streamMonitor);
    string? line;
    while ((line = await readerMonitor.ReadLineAsync()) != null)
    {
        var dp = System.Text.Json.JsonSerializer.Deserialize<DataPointDto>(line);
        restCount++;
    }
    sw.Stop();
    var restTotalMs = sw.ElapsedMilliseconds;

    results.Add((
        "Monitor (1000 msgs)",
        grpcTotalMs,
        restTotalMs,
        $"gRPC Count={grpcCount}, REST Count={restCount}"
    ));



    // 4. SendAndMonitor (bi-directional streaming)

    // gRPC SendAndMonitor
    var grpcBidiLatencies = new List<long>();
    var lastTimestamp = Stopwatch.GetTimestamp();
    using var bidiCall = grpcClient.SendAndMonitor();

    // send task
    var sendTask = Task.Run(async () =>
    {
        for (int i = 0; i < 5; i++)
        {
            await bidiCall.RequestStream.WriteAsync(new DataPoint
            {
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                PartNumber = $"PN-{i}",
                SpecNominal = 10.0,
                SpecTolerance = 0.5,
                Measurement = 9.5 + i * 0.1
            });
            await Task.Delay(500); // simulate delay
        }
        await bidiCall.RequestStream.CompleteAsync();
    });

    int grpcResponses = 0;
    await foreach (var dp in bidiCall.ResponseStream.ReadAllAsync())
    {
        grpcResponses++;
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - lastTimestamp) * 1000 / Stopwatch.Frequency;
        grpcBidiLatencies.Add(elapsed);
        lastTimestamp = now;
    }

    // REST SendAndMonitor (streaming JSON)
    var restBidiLatencies = new List<long>();
    lastTimestamp = Stopwatch.GetTimestamp();

    var payload = new[]
    {
    new DataPointDto(DateTime.UtcNow,"PN-1",10,0.5,9.7),
    new DataPointDto(DateTime.UtcNow,"PN-2",10,0.5,9.8),
    new DataPointDto(DateTime.UtcNow,"PN-3",10,0.5,9.9),
    new DataPointDto(DateTime.UtcNow,"PN-4",10,0.5,9.9),
    new DataPointDto(DateTime.UtcNow,"PN-5",10,0.5,9.9),
};

    var response = await httpClient.PostAsJsonAsync("/sendandmonitor", payload);
    using var stream = await response.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(stream);
    using var jsonReader = new Newtonsoft.Json.JsonTextReader(reader);

    int restResponses = 0;

    while (await jsonReader.ReadAsync())
    {
        if (jsonReader.TokenType == Newtonsoft.Json.JsonToken.StartObject)
        {
            restResponses++;
            var now = Stopwatch.GetTimestamp();
            var elapsed = (now - lastTimestamp) * 1000 / Stopwatch.Frequency;
            restBidiLatencies.Add(elapsed);
            lastTimestamp = now;
        }
    }
    results.Add((
        "SendAndMonitor (per‑msg)",
        (long)grpcBidiLatencies.Average(),
        (long)restBidiLatencies.Average(),
        $"gRPC Resp={grpcResponses}, REST Resp={restResponses}"
    ));


    // Print results in side-by-side table with icons
    // Define column widths
    int col1Width = 25;
    int col2Width = 18;
    int col3Width = 18;
    int col4Width = 40;
    Console.WriteLine();
    Console.WriteLine(
        $"{"Method".PadRight(col1Width)}" +
        $"{"⚡ gRPC Time (ms)".PadRight(col2Width)}" +
        $"{"🌐 REST Time (ms)".PadRight(col3Width)}" +
        $"{"Output".PadRight(col4Width)}");

    Console.WriteLine(new string('-', col1Width + col2Width + col3Width + col4Width));

    foreach (var r in results)
    {
        Console.WriteLine(
            $"{r.Method.PadRight(col1Width)}" +
            $"{r.GrpcTime.ToString().PadRight(col2Width)}" +
            $"{r.RestTime.ToString().PadRight(col3Width)}" +
            $"{r.Output.PadRight(col4Width)}");
    }
}