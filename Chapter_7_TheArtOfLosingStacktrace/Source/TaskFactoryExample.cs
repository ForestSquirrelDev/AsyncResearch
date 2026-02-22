namespace AsyncResearch.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public static class TaskFactoryExample
    {
        public static async Task RunTaskFactoryExample()
        {
            var task = Task.Factory.StartNew(async () => {
                await Task.Delay(100);
                throw new Exception("HORY SHIET!");
            });
            await await task;
        }
    }
}