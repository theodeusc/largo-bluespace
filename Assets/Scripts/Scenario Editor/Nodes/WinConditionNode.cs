using System.Collections.Generic;
using Glitchers.EcoKnow.Sandbox;
using UnityEngine;
using XNode;

public record WinConditionRecord
(
    string Title,
    string Description,
    int TypeIndex,
    int TargetIndex,
    float LowerLimit,
    float UpperLimit,
    int RequiredRounds,
    // When true, the win condition only matters on the scenario's final round and skips
    // per-round tracking entirely. Useful for end-state checks (e.g. eColi load) where
    // mid-game pollution peaks are expected and shouldn't permanently fail the win once
    // the player has had a chance to build treatment / let declines wind back the level.
    bool FinalRoundOnly = false
);

public class WinConditionNode : Node
{
    [Output(ShowBackingValue.Never, ConnectionType.Override)] [SerializeField] private string _id;


    [SerializeField] protected string title;
    [SerializeField, Multiline]
    protected string description;

    [SerializeField, HideInInspector] protected int typeIndex;
    public int TypeIndex { get { return typeIndex; } set { typeIndex = value; } }

    [SerializeField, HideInInspector] protected int targetIndex;
    public int TargetIndex { get { return targetIndex; } set { targetIndex = value; } }

    [SerializeField] protected float lowerLimit;
    [SerializeField] protected float upperLimit;

    [SerializeField] protected int requiredRounds = 1; //consistency across rounds

    public List<Entity> AvailableEntities
    {
        get
        {
            //Are we connected to the main scenario node?
            NodePort outputPort = GetOutputPort("_id");
            if ((outputPort != null) &&
                (outputPort.IsConnected) &&
                (outputPort.Connection.node.GetType() == typeof(ScenarioNode)))
            {
                //Return valid entity list from graph
                if (graph is ScenarioNodeGraph scenarioGraph)
                {
                    return scenarioGraph.GetEntityList();
                }
            }

            return null;
        }
    }

    public List<Item> AvailableItems
    {
        get
        {
            //Return valid item list from graph
            if (graph is ScenarioNodeGraph scenarioGraph)
            {
                return scenarioGraph.GetScenarioNode()?.ItemDefs;
            }

            return null;
        }
    }

    protected override void Init()
    {
        base.Init();
    }

    private void OnValidate()
    {
        NodePort inputPort = GetInputPort("_id");
        if (inputPort != null)
        {
            if (inputPort.IsConnected)
            {
                Debug.Log($"input port type = {inputPort.Connection.node.GetType()}");
            }
        }
    }

    public override object GetValue(NodePort port)
    {
        if (port.IsInput)
        {
            Debug.Log($"input port type = {port.GetType()}");
        }

        return null;
    }

    public WinConditionRecord GetWinCondition()
    {
        return new WinConditionRecord(title, description, typeIndex, targetIndex, lowerLimit, upperLimit, requiredRounds);
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

    public bool IsRangeValid()
    {
        if ((upperLimit > 0) && (upperLimit < lowerLimit))
        {
            return false;
        }

        return true;
    }
}
