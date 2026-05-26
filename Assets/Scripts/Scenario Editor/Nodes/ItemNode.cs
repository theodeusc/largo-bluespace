using Glitchers.EcoKnow.Sandbox;
using UnityEditor;
using UnityEngine;
using XNode;

public class ItemNode : Node
{

    [Output(ShowBackingValue.Always, ConnectionType.Override)] [SerializeField] private string _id;
    public string ID => _id;

    [SerializeField] private Sprite _icon;
    private string _iconPath
    {
        get
        {
#if UNITY_EDITOR
            if (_icon != null)
            {
                string path = AssetDatabase.GetAssetPath(_icon);
                int resourcesIndex = path.IndexOf("Resources/") + "Resources/".Length;
                int extensionIndex = path.LastIndexOf(".");
                if (resourcesIndex >= 0)
                {
                    path = path.Substring(resourcesIndex, extensionIndex - resourcesIndex);
                    return path;
                }
            }
#endif
            return null;
        }
    }


    [SerializeField] private int _value;
    [SerializeField] private bool _canSell;
    [SerializeField] private int _buyPrice = 0;
    [SerializeField] private int _maxQuantity = 0;



    // Use this for initialization
    protected override void Init()
    {
        base.Init();

    }

    // Return the correct value of an output port when requested
    public override object GetValue(NodePort port)
    {
        return null; // Replace this
    }

    public Item GetItem()
    {
        return new Item(_id, _iconPath, _value, _canSell, _buyPrice, _maxQuantity);
    }

    public bool IsConnected()
    {
        NodePort port = GetOutputPort("_id");
        if ((port != null) && (port.IsConnected))
        {
            if (port.Connection.node is ScenarioNode scenario)
            {
                return true;
            }
        }

        return false;
    }
}
