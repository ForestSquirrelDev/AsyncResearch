using AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace;
using AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source;

namespace AsyncResearch
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            await TcsException.TcsExceptionTest();
        }
    }
}