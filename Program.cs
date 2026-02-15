using AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        { 
            await ThrowExExample.RunThrowExExample();
        }

        private static async Task MyAsyncMethod()
        {
            Console.WriteLine("MyAsyncMethod");
        }
    }
}