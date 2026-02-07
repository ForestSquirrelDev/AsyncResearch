using AsyncResearch.AsyncExperiments.Chapter_X_Additionals;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await ForceYieldingInfiniteLoopExample.Test();
        }
    }
}