Пожалуй, самый простой способ потерять стактрейс (даже проще чем потерянный `awaiter` у `async void`) - это сделать `throw ex` в блоке `catch`. Возьмём пример:
````csharp
namespace AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source
{
    public static class ThrowExExample
    {
        public static async Task RunThrowExExample()
        {
            await Layer0();
            await Task.Delay(1000);
        }

        private static async Task Layer0()
        {
            await Task.Delay(100);
            await Layer1();
        }

        private static async Task Layer1()
        {
            await Task.Delay(100);
            try
            {
                await Layer2();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        
        private static async Task Layer2()
        {
            await Task.Delay(100);
            await Layer3();
        }

        private static async Task Layer3()
        {
            await Task.Delay(100);
            await Layer4();
        }
        
        private static async Task Layer4()
        {
            await Task.Delay(100);
            throw new Exception("HORY SHIET!");
        }
    }
}
````

Мы вызвали несколько вложенных асинхронных методов. В последнем вызове `Layer4()` выбрасывается исключение, а в `Layer1()` мы ловим и пробрасываем его заново. Когда мы пишем `throw ex`,
рантайм .NET воспринимает это как новое исключение из данного метода. Оригинальный стактрейс заменится на `Layer1()`:
````
Unhandled exception. System.Exception: HORY SHIET!
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.ThrowExExample.Layer1()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.ThrowExExample.Layer0()
   at AsyncResearch.AsyncExperiments.Chapter_7_TheArtOfLosingStacktrace.Source.ThrowExExample.RunThrowExExample()
   at AsyncResearch.Program.Main(String[] args)
   at AsyncResearch.Program.<Main>(String[] args)

Process finished with exit code -532,462,766.
````

Даже IDE подсказывает нам об этом, говоря: `Re-throwing caught exception changes stack information`.
Исправить это можно двумя способами.
1. Сделать `throw`. Рантайм посчитает, что мы хотим перевыбросить исключение, и сохранит оригинальный стактрейс:
````csharp
...
try
{
    await Layer2();
}
catch (Exception ex)
{
    throw;
}
...
````
2. Использовать `EDI`. Он приклеит стактрейс `Layer1()` к оригинальному исключению:
````csharp
...
try
{
    await Layer2();
}
catch (Exception ex)
{
    ExceptionDispatchInfo.Capture(ex).Throw();
}
...
````