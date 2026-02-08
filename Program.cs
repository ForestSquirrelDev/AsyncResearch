using AsyncResearch.AsyncExperiments.Chapter_1_HeartOfTheStateMachine;
using AsyncResearch.AsyncExperiments.Chapter_5_Additionals;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await AsyncSurvivorCaller.Test();
            Console.WriteLine("Hello, World!");
        }
    }
}