using AsyncResearch.AsyncExperiments.Chapter_3_SynchronizationContext;

namespace AsyncResearch
{
    public class Program
    {
        public static Task Main(string[] args)
        {
            SynchronizationContextExample.Run();
            return Task.CompletedTask;
        }
    }
}