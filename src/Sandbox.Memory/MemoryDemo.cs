namespace Sandbox.Memory;

public struct CommitRecordStruct
{
    public string Hash;
    public int FilesCount;

    public CommitRecordStruct(string hash, int filesCount)
    {
        Hash = hash;
        FilesCount = filesCount;
    }
}

public class CommitRecordClass
{
    public string Hash;
    public int FilesCount;

    public CommitRecordClass(string hash, int filesCount)
    {
        Hash = hash;
        FilesCount = filesCount;
    }
}

public static class MemoryDemo
{
    public static void Run()
    {
        Console.WriteLine("--- Struct (значимый тип) ---");
        CommitRecordStruct s = new CommitRecordStruct("a1b2c3", 3);
        Console.WriteLine($"Старт:                  {s.Hash}, файлов: {s.FilesCount}");

        ChangeStructField(s);
        Console.WriteLine($"После ChangeField:      {s.Hash}, файлов: {s.FilesCount}");

        ChangeStructFieldRef(ref s);
        Console.WriteLine($"После ChangeFieldRef:   {s.Hash}, файлов: {s.FilesCount}");

        ReplaceStruct(s);
        Console.WriteLine($"После Replace:          {s.Hash}, файлов: {s.FilesCount}");

        ReplaceStructRef(ref s);
        Console.WriteLine($"После ReplaceRef:       {s.Hash}, файлов: {s.FilesCount}");

        Console.WriteLine();
        Console.WriteLine("--- Class (ссылочный тип) ---");
        CommitRecordClass c = new CommitRecordClass("a1b2c3", 3);
        Console.WriteLine($"Старт:                  {c.Hash}, файлов: {c.FilesCount}");

        ChangeClassField(c);
        Console.WriteLine($"После ChangeField:      {c.Hash}, файлов: {c.FilesCount}");

        ReplaceClass(c);
        Console.WriteLine($"После Replace:          {c.Hash}, файлов: {c.FilesCount}");

        ReplaceClassRef(ref c);
        Console.WriteLine($"После ReplaceRef:       {c.Hash}, файлов: {c.FilesCount}");
    }

    static void ChangeStructField(CommitRecordStruct item)
    {
        item.FilesCount = 10;
    }

    static void ChangeStructFieldRef(ref CommitRecordStruct item)
    {
        item.FilesCount = 10;
    }

    static void ReplaceStruct(CommitRecordStruct item)
    {
        item = new CommitRecordStruct("ffffff", 99);
    }

    static void ReplaceStructRef(ref CommitRecordStruct item)
    {
        item = new CommitRecordStruct("ffffff", 99);
    }

    static void ChangeClassField(CommitRecordClass item)
    {
        item.FilesCount = 10;
    }

    static void ReplaceClass(CommitRecordClass item)
    {
        item = new CommitRecordClass("ffffff", 99);
    }

    static void ReplaceClassRef(ref CommitRecordClass item)
    {
        item = new CommitRecordClass("ffffff", 99);
    }
}
