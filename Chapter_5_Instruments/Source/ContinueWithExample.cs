namespace AsyncResearch.Chapter_5_Instruments.Source
{
    public static class ContinueWithExample
    {
        public static async Task RunContinueWithExample()
        {
            await Task.Delay(1000).ContinueWith(task => Console.WriteLine("Hello, World!"));
        }
    }
}