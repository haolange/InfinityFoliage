using Unity.Jobs;
using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine.Rendering;
using System.Collections.Generic;
using InfinityTech.Core.Geometry;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

#if UNITY_EDITOR
using UnityEditor;
#endif

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

//[ExecuteInEditMode]
#if UNITY_EDITOR
[CanEditMultipleObjects]
#endif
public abstract unsafe class FoliageComponent : MonoBehaviour
{
    [HideInInspector]
    public Transform EntityTransform;

    [HideInInspector]
    internal RenderTransfrom CurrTransform;

    [HideInInspector]
    internal RenderTransfrom LastTransform;
    
    public static List<FoliageComponent> s_foliageComponents = new List<FoliageComponent>(128);

    
    void OnEnable()
    {
        s_foliageComponents.Add(this);
        EntityTransform = GetComponent<Transform>();
        OnRegister();
        EventPlay();
    }

    void EventUpdate()
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
        s_foliageComponents.Remove(this);
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
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract void InitViewFoliage(in float3 viewPos, in float4x4 matrixProj, FPlane* planes, in NativeList<JobHandle> taskHandles);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract void DispatchSetup(in NativeList<JobHandle> taskHandles);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public abstract void DispatchDraw(CommandBuffer cmdBuffer);
}
