namespace AsyncResearch.Chapter_4_AsyncVoid.Source
{
    public class ThreadPoolException
    {
        public static void Test()
        {
            Console.WriteLine("Test: Start");
            DoWorkAsync();
            Console.WriteLine("Test: End");
        }
        
        private static async void DoWorkAsync()
        {
            var localVariable = 42;
            await Task.Delay(1000);
            Console.WriteLine("Resumed with " + localVariable);
            throw new Exception("DoWorkAsync: HORY SHIET!");
        }
    }
}