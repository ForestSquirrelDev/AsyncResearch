namespace AsyncResearch.Chapter_5_Instruments.Source
{
    public static class TaskRunExample
    {
        public static async Task RunTaskRunExample()
        {
            await Task.Run(() =>
            {
                Console.WriteLine("Hello World!");
            });
        }
    }
}