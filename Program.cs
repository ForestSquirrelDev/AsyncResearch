using AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await AsyncVoidNestedException.AsyncVoidExceptionTest();
        }
    }
}