using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HelperFunctions
{
    /// <summary>
    /// Computes cumulative weights for linearly decreasing likelihoods based on index.
    /// </summary>
    /// <param name="arrayLength">The length of the array.</param>
    /// <returns>An array of cumulative weights.</returns>
    public static float[] ComputeCumulativeWeights(int arrayLength)
    {
        if (arrayLength == 0) return new float[0];

        float[] weights = new float[arrayLength];
        float totalWeight = 0f;

        // Assign weights based on index: higher index = lower weight
        for (int i = 0; i < arrayLength; i++)
        {
            weights[i] = arrayLength - i; // Weight decreases with index
            totalWeight += weights[i];
        }

        // Normalize weights and compute cumulative weights
        for (int i = 0; i < arrayLength; i++)
        {
            weights[i] /= totalWeight;
            if (i > 0) weights[i] += weights[i - 1];
        }

        return weights;
    }

    /// <summary>
    /// Selects an object from the array based on linearly decreasing likelihoods by index.
    /// </summary>
    /// <param name="objectsArray">The array of objects to choose from.</param>
    /// <returns>The selected object.</returns>
    public static GameObject SelectBasedOnIndexPriority(GameObject[] objectsArray)
    {
        if (objectsArray == null || objectsArray.Length == 0) return null;

        float[] cumulativeWeights = ComputeCumulativeWeights(objectsArray.Length);

        // Generate a random number between 0 and 1
        float randomValue = Random.value;

        // Find the selected index
        for (int i = 0; i < cumulativeWeights.Length; i++)
        {
            if (randomValue <= cumulativeWeights[i])
            {
                return objectsArray[i];
            }
        }

        return objectsArray[objectsArray.Length - 1]; // Fallback to last object
    }
}
