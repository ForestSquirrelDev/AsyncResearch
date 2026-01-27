namespace AsyncResearch.AsyncExperiments.ChapterTwo_AnatomyOfCrash
{
    public class AsyncTryCatchExample
    {
        public static async Task Test()
        {
            Console.WriteLine("Start");
            await DoWorkAsync();
            throw new Exception("HORY SHET!");
        }
        
        public static async Task DoWorkAsync()
        {
            var localVariable = 42;
            await Task.Delay(10000);
            Console.WriteLine("Resumed with " + localVariable);
        }
    }
}