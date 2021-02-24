using UnityEditor;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

internal struct RenderTransfrom
{
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 scale;

    public override int GetHashCode()
    {
        return position.GetHashCode() + rotation.GetHashCode() + scale.GetHashCode();
    }

    public override bool Equals(object obj)
    {
        RenderTransfrom target = (RenderTransfrom)obj;
        return position != target.position || rotation != target.rotation || scale != target.scale;
    }

    public bool Equals(RenderTransfrom target)
    {
        return position != target.position || rotation != target.rotation || scale != target.scale;
    }
};

[ExecuteInEditMode]
#if UNITY_EDITOR
[CanEditMultipleObjects]
#endif
public class InstanceMeshComponent : MonoBehaviour
{
    // Public Variable
    [HideInInspector]
    public Transform EntityTransform;

    [HideInInspector]
    internal RenderTransfrom CurrTransform;

    [HideInInspector]
    internal RenderTransfrom LastTransform;


    // Function
    public InstanceMeshComponent() { }

    void OnEnable()
    {
        EntityTransform = GetComponent<Transform>();
        OnRegister();
        EventPlay();
    }

    void Update()
    {
        if (TransfromStateDirty())
        {
            OnTransformChange();
        }
        EventTick();
    }

    void OnDisable()
    {
        UnRegister();
    }

    private bool TransfromStateDirty()
    {
        CurrTransform.position = EntityTransform.position;
        CurrTransform.rotation = EntityTransform.rotation;
        CurrTransform.scale = EntityTransform.localScale;

        if (CurrTransform.Equals(LastTransform))
        {
            LastTransform = CurrTransform;
            return true;
        }

        return false;
    }

    protected virtual void OnRegister()
    {

    }

    protected virtual void EventPlay()
    {

    }

    protected virtual void EventTick()
    {

    }

    protected virtual void OnTransformChange()
    {

    }

    protected virtual void UnRegister()
    {

    }
}
