using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OxyPlot;
using OxyPlot.ImageSharp;
using OxyPlot.Series;

public class Server
{
    private readonly bool[] channels;
    private readonly object lockObj = new object();
    private readonly double mu;
    private int totalRequests;
    private int handledRequests;
    private int rejectedRequests;
    private double totalProcessingTime;
    private DateTime simulationStart;
    private DateTime simulationEnd;
    private double idleTime;
    private bool[] prevChannelState;
    private DateTime lastUpdate;

    public Server(int channelCount, double mu)
    {
        this.mu = mu;
        channels = new bool[channelCount];
        prevChannelState = new bool[channelCount];
    }

    public int TotalRequests => totalRequests;
    public int HandledRequests => handledRequests;
    public int RejectedRequests => rejectedRequests;
    public double TotalProcessingTime => totalProcessingTime;
    public TimeSpan SimulationTime => simulationEnd - simulationStart;
    public double IdleTime => idleTime;

    public void StartSimulation()
    {
        simulationStart = DateTime.Now;
        lastUpdate = simulationStart;
        prevChannelState = new bool[channels.Length];
        idleTime = 0;
    }

    public void StopSimulation()
    {
        simulationEnd = DateTime.Now;
        UpdateIdle();
    }

    private void UpdateIdle()
    {
        lock (lockObj)
        {
            DateTime now = DateTime.Now;
            double elapsed = (now - lastUpdate).TotalSeconds;

            bool wasIdle = Array.TrueForAll(prevChannelState, c => !c);
            if (wasIdle) idleTime += elapsed;

            lastUpdate = now;
            Array.Copy(channels, prevChannelState, channels.Length);
        }
    }

    public void HandleRequest(object sender, EventArgs e)
    {
        Interlocked.Increment(ref totalRequests);
        lock (lockObj)
        {
            UpdateIdle();

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

    private void ProcessRequest(int channelIdx)
    {
        DateTime start = DateTime.Now;
        double delay = GenerateExponential(mu);
        Thread.Sleep((int)(delay * 1000));

        lock (lockObj)
        {
            UpdateIdle();
            channels[channelIdx] = false;
            totalProcessingTime += (DateTime.Now - start).TotalSeconds;
        }
    }

    private double GenerateExponential(double rate)
    {
        Random rand = new Random(Guid.NewGuid().GetHashCode());
        return -Math.Log(1 - rand.NextDouble()) / rate;
    }
}

public class Client
{
    private readonly double lambda;
    private volatile bool running;
    public event EventHandler RequestGenerated;

    public Client(double lambda)
    {
        this.lambda = lambda;
        running = false;
    }

    public void Start()
    {
        running = true;
        Task.Run(() =>
        {
            while (running)
            {
                double interval = GenerateExponential(lambda);
                Thread.Sleep((int)(interval * 1000));
                RequestGenerated?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    public void Stop() => running = false;

    private double GenerateExponential(double rate)
    {
        Random rand = new Random(Guid.NewGuid().GetHashCode());
        return -Math.Log(1 - rand.NextDouble()) / rate;
    }
}

class Program
{
    static void Main()
    {
        int n = 3; // Количество каналов
        double mu = 1.0; // Интенсивность обслуживания
        double[] lambdas = { 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0 };

        var results = new List<SimDataPoint>();

        foreach (var lambda in lambdas)
        {
            var server = new Server(n, mu);
            var client = new Client(lambda);
            client.RequestGenerated += server.HandleRequest;

            server.StartSimulation();
            client.Start();
            Thread.Sleep(30000); // 30 секунд моделирования
            client.Stop();
            server.StopSimulation();

            // Экспериментальные значения
            double expP0 = server.IdleTime / server.SimulationTime.TotalSeconds;
            double expPreject = (double)server.RejectedRequests / server.TotalRequests;
            double expQ = 1 - expPreject;
            double expA = lambda * expQ;
            double expN = (lambda / mu) * expQ;

            // Теоретические значения
            double theoryP0 = CalcTheoryP0(lambda, mu, n);
            double theoryPreject = CalcTheoryPreject(lambda, mu, n);
            double theoryQ = 1 - theoryPreject;
            double theoryA = lambda * theoryQ;
            double theoryN = (lambda / mu) * theoryQ;

            results.Add(new SimDataPoint(
                lambda,
                expP0, theoryP0,
                expPreject, theoryPreject,
                expQ, theoryQ,
                expA, theoryA,
                expN, theoryN
            ));
        }

        // Генерация графиков
        GeneratePlot(results, "p-1.png", "Вероятность простоя",
            p => p.ExpP0, p => p.TheoryP0);
        GeneratePlot(results, "p-2.png", "Вероятность отказа",
            p => p.ExpPreject, p => p.TheoryPreject);
        GeneratePlot(results, "p-3.png", "Относительная пропускная способность",
            p => p.ExpQ, p => p.TheoryQ);
        GeneratePlot(results, "p-4.png", "Абсолютная пропускная способность",
            p => p.ExpA, p => p.TheoryA);
        GeneratePlot(results, "p-5.png", "Среднее число занятых каналов",
            p => p.ExpN, p => p.TheoryN);
    }

    static double CalcTheoryP0(double lambda, double mu, int n)
    {
        double rho = lambda / mu;
        double sum = 0;
        for (int k = 0; k <= n; k++)
            sum += Math.Pow(rho, k) / Factorial(k);
        return 1 / sum;
    }

    static double CalcTheoryPreject(double lambda, double mu, int n)
    {
        double rho = lambda / mu;
        return (Math.Pow(rho, n) / Factorial(n)) * CalcTheoryP0(lambda, mu, n);
    }

    static double Factorial(int k) => k <= 1 ? 1 : k * Factorial(k - 1);

    static void GeneratePlot(List<SimDataPoint> data, string filename, string title,
        Func<SimDataPoint, double> expSelector, Func<SimDataPoint, double> theorySelector)
    {
        var model = new PlotModel { Title = title };
        var expLine = new LineSeries { Title = "Эксперимент" };
        var theoryLine = new LineSeries { Title = "Теория" };

        foreach (var point in data)
        {
            expLine.Points.Add(new DataPoint(point.Lambda, expSelector(point)));
            theoryLine.Points.Add(new DataPoint(point.Lambda, theorySelector(point)));
        }

        model.Series.Add(expLine);
        model.Series.Add(theoryLine);

        var exporter = new PngExporter(800,600);
        using (var stream = File.Create(Path.Combine("..//..//..//..//..//result", filename)))
            exporter.Export(model, stream);
    }
}

record SimDataPoint(
    double Lambda,
    double ExpP0, double TheoryP0,
    double ExpPreject, double TheoryPreject,
    double ExpQ, double TheoryQ,
    double ExpA, double TheoryA,
    double ExpN, double TheoryN
);