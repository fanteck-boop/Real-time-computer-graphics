using UnityEngine;

public class Transformations : MonoBehaviour
{
    CreateGrid createGrid;

    Vector3[] startPos;

    [Header("Movement Controls")]
    public float moveFrequency = 2f;
    public float moveAmplitude = 5f;
    public float moveOffset = 0f;

    Vector3 startScale = Vector3.zero;

    [Header("Scale Controls")]
    public float scaleFrequency = 2f;
    public float scaleAmplitude = 5f;
    public float scaleOffset = 0f;

    [Header("Custom tweak: cos-based pulse")]
    // When on, each cube pulses between 0.4x and 1.6x of its start size with a phase that depends on
    // its grid index, so neighbours stay distinct and a gap remains for shadows. Off = original tutorial maths.
    public bool cosPulse = false;
    public float pulseAmount = 0.6f;
    public float pulsePhasePerCube = 0.07f;
    public float pulseScale = 1f;      // overall size multiplier of the pulse (1 = start scale x (1 +- pulseAmount))

    [Header("Rotation Controls")]
    public float rotationSpeed = 1f;

    private void Start()
    {
        createGrid = GetComponent<CreateGrid>();
        startPos = createGrid.startPos;
        startScale = createGrid.grid[0].localScale;
    }

    void Update()
    {
        for (int i = 0, z = 0; z < createGrid.gridResolution; z++)
        {
            for (int y = 0; y < createGrid.gridResolution; y++)
            {
                for (int x = 0; x < createGrid.gridResolution; x++, i++)
                {
                    createGrid.grid[i].localPosition = startPos[i] + Vector3.up * Mathf.Sin(Time.time * moveFrequency + moveOffset) * moveAmplitude;

                    if (cosPulse)
                        createGrid.grid[i].localScale = startScale * pulseScale * (1f + pulseAmount * Mathf.Cos(Time.time * scaleFrequency + i * pulsePhasePerCube));
                    else
                        createGrid.grid[i].localScale = startScale * Mathf.Sin(Time.time * scaleFrequency) * scaleAmplitude + Vector3.one * scaleOffset;

                    createGrid.grid[i].Rotate(Vector3.forward * Time.deltaTime * rotationSpeed);
                }
            }
        }
    }
}
