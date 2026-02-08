using AsyncResearch.AsyncExperiments.Chapter_1_HeartOfTheStateMachine;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await ContinuationExample.Test();
            Console.WriteLine("Hello, World!");
        }
    }
}