namespace AqwCompanion.Phone.Notices;

/// <summary>Told about every notice the engine admitted, under the hub's lock: it must return quickly and never throw.</summary>
public interface INoticeSink
{
    void Publish(Notice notice);
}
