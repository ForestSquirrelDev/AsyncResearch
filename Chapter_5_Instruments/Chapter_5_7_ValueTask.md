Вместо `Task`, в методе можно вернуть `ValueTask`:
````csharp
public static ValueTask RunValueTaskExample()
{
    return ValueTask.CompletedTask;
}
````