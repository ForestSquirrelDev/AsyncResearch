using AsyncResearch.AsyncExperiments.ChapterOne;
using AsyncResearch.AsyncExperiments.ChapterTwo_AnatomyOfCrash;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await AsyncTryCatchExample.Test();
            Console.WriteLine("Hello, World!");
        }
    }
}