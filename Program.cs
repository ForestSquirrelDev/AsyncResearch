using AsyncResearch.AsyncExperiments.Chapter_2_AnatomyOfCrash;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await TaskUnhandledExceptionExample.Test();
            Console.WriteLine("Hello, World!");
        }
    }
}