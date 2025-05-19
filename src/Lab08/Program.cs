using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OxyPlot.ImageSharp;
using OxyPlot.Series;
using OxyPlot;

public class Server
{
    private readonly bool[] channels;
    private readonly object lockObject = new object();
    private readonly double mu;
    private int totalRequests;
    private int handledRequests;
    private int rejectedRequests;
    private double totalProcessingTime;
    private DateTime simulationStartTime;
    private DateTime simulationEndTime;
    private double idleTime;
    private bool[] previousChannelState;
    private DateTime lastUpdateTime;

    public Server(int channelCount, double mu)
    {
        this.mu = mu;
        channels = new bool[channelCount];
        previousChannelState = new bool[channelCount];
    }

    public int TotalRequests => totalRequests;
    public int HandledRequests => handledRequests;
    public int RejectedRequests => rejectedRequests;
    public double TotalProcessingTime => totalProcessingTime;
    public TimeSpan SimulationDuration => simulationEndTime - simulationStartTime;
    public double IdleTime => idleTime;

    public void StartSimulation()
    {
        simulationStartTime = DateTime.Now;
        lastUpdateTime = simulationStartTime;
        previousChannelState = new bool[channels.Length];
        idleTime = 0;
    }

    public void StopSimulation()
    {
        simulationEndTime = DateTime.Now;
        UpdateIdleTime();
    }

    private void UpdateIdleTime()
    {
        lock (lockObject)
        {
            DateTime now = DateTime.Now;
            double elapsed = (now - lastUpdateTime).TotalSeconds;

            bool wasIdle = previousChannelState.All(c => !c);
            if (wasIdle)
                idleTime += elapsed;

            lastUpdateTime = now;
            previousChannelState = channels.ToArray();
        }
    }

    public void HandleRequest(object sender, EventArgs e)
    {
        Interlocked.Increment(ref totalRequests);
        lock (lockObject)
        {
            UpdateIdleTime();

            for (int i = 0; i < channels.Length; i++)
            {
                if (!channels[i])
                {
                    channels[i] = true;
                    Task.Run(() => ProcessRequest(i));
                    Interlocked.Increment(ref handledRequests);
                    return;
                }
            }
            Interlocked.Increment(ref rejectedRequests);
        }
    }

    private void ProcessRequest(int channelIndex)
    {
        DateTime start = DateTime.Now;
        double processingTime = GenerateExponential(mu);
        int delay = (int)(processingTime * 1000);
        Thread.Sleep(delay);

        lock (lockObject)
        {
            UpdateIdleTime();
            channels[channelIndex] = false;
            totalProcessingTime += (DateTime.Now - start).TotalSeconds;
        }
    }

    private double GenerateExponential(double rate)
    {
        Random rand = new Random(Guid.NewGuid().GetHashCode());
        double u = rand.NextDouble();
        return -Math.Log(1 - u) / rate;
    }
}
public class Client
{
    private readonly double lambda;
    private volatile bool isRunning;
    public event EventHandler RequestGenerated;

    public Client(double lambda)
    {
        this.lambda = lambda;
        isRunning = false;
    }

    public void Start()
    {
        isRunning = true;
        Task.Run(() =>
        {
            while (isRunning)
            {
                double interval = GenerateExponential(lambda);
                Thread.Sleep((int)(interval * 1000));
                RequestGenerated?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    public void Stop()
    {
        isRunning = false;
    }

    private double GenerateExponential(double rate)
    {
        Random rand = new Random(Guid.NewGuid().GetHashCode());
        double u = rand.NextDouble();
        return -Math.Log(1 - u) / rate;
    }
}


class Program
{
    static void Main()
    {
        int n = 3; // Количество каналов
        double mu = 1.0; // Интенсивность обслуживания
        double[] lambdas = new double[] { 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0 }; // Интенсивности входного потока

        List<SimulationDataPoint> results = new List<SimulationDataPoint>();

        foreach (double lambda in lambdas)
        {
            var server = new Server(n, mu);
            var client = new Client(lambda);
            client.RequestGenerated += server.HandleRequest;

            server.StartSimulation();
            client.Start();
            Thread.Sleep(30000); // 30 секунд симуляции
            client.Stop();
            server.StopSimulation();

            double expP0 = server.IdleTime / server.SimulationDuration.TotalSeconds;
            double theoryP0 = CalculateTheoreticalP0(lambda, mu, n);
            results.Add(new SimulationDataPoint
            {
                Lambda = lambda,
                ExperimentalP0 = expP0,
                TheoreticalP0 = theoryP0
            });
        }

        GeneratePlot(results, "p-1.png", "Вероятность простоя");
    }

    static double CalculateTheoreticalP0(double lambda, double mu, int n)
    {
        double rho = lambda / mu;
        double sum = 0;
        for (int k = 0; k <= n; k++)
            sum += Math.Pow(rho, k) / Factorial(k);
        return 1 / sum;
    }

    static double Factorial(int k)
    {
        return k <= 1 ? 1 : k * Factorial(k - 1);
    }

    static void GeneratePlot(List<SimulationDataPoint> data, string filename, string title)
    {
        var plotModel = new PlotModel { Title = title };
        var expSeries = new LineSeries { Title = "Экспериментальная" };
        var theorySeries = new LineSeries { Title = "Теоретическая" };

        foreach (var point in data)
        {
            // Используем DataPoint из OxyPlot с конструктором (x, y)
            expSeries.Points.Add(new OxyPlot.DataPoint(point.Lambda, point.ExperimentalP0));
            theorySeries.Points.Add(new OxyPlot.DataPoint(point.Lambda, point.TheoreticalP0));
        }

        plotModel.Series.Add(expSeries);
        plotModel.Series.Add(theorySeries);

        var exporter = new PngExporter (800, 600);
        using (var stream = File.Create(Path.Combine("..//..//..//..//..//result", filename)))
            exporter.Export(plotModel, stream);
    }
}

class SimulationDataPoint
{
    public double Lambda { get; set; }
    public double ExperimentalP0 { get; set; }
    public double TheoreticalP0 { get; set; }
    // Другие показатели
}