namespace GuardianConnect.Abstractions;

public class CompositeType
{
    bool boolValue = true;
    string stringValue = "Hello ";

    public bool BoolValue
    {
        get { return boolValue; }
        set { boolValue = value; }
    }

    public string StringValue
    {
        get { return stringValue; }
        set { stringValue = value; }
    }
}