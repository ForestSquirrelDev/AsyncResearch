namespace AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public static class TaskWhenAllForeachExample
    {
        public static async Task WhenAllExceptionsExample()
        {
            var t1 = Exception1();
            var t2 = Exception2();
            var t3 = Exception3();
            
            var allTasks = Task.WhenAll(t1, t2, t3);
            try
            {
                await allTasks;
            }
            catch
            {
                var realExceptions = allTasks.Exception.InnerExceptions;
                for (var i = 0; i < realExceptions.Count; i++)
                {
                    var exception = realExceptions[i];
                    Console.WriteLine($"\nИсключение номер {i + 1}: {exception}");
                }
            }
        }

        private static async Task Exception1()
        {
            throw new Exception("Exception1");
        }
        
        private static async Task Exception2()
        {
            throw new Exception("Exception2");
        }
        
        private static async Task Exception3()
        {
            throw new Exception("Exception3");
        }
    }
}