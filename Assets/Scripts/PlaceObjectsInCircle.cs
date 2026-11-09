using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlaceObjectsInCircle : MonoBehaviour
{
    public GameObject stonePrefab;
    public int numberOfStones = 10; // Number of stones to place
    public float radius = 20f;      // Radius of the circle
    public Transform player;        // Reference to the playe
    public float size = 1f;

    void Start()
    {
        radius = GameSettings.circleRadius;
        PlaceStonesAroundPlayer();
    }

    void PlaceStonesAroundPlayer()
    {
        for (int i = 0; i < numberOfStones; i++)
        {
            // Calculate the angle for this stone
            float angle = i * Mathf.PI * 2 / numberOfStones;

            // Calculate the position on the circle
            float x = Mathf.Cos(angle) * radius;
            float z = Mathf.Sin(angle) * radius;

            // Create the stone at the calculated position relative to the player
            Vector3 stonePosition = new Vector3(x, - player.position.y, z) + player.position;

            // Instantiate the stonePrefab at the calculated position and with no rotation
            GameObject stone = Instantiate(stonePrefab, stonePosition, Quaternion.identity);
            stone.transform.localScale *= size;

            Vector3 directionToCenter = (player.position - stone.transform.position).normalized;
            stone.transform.rotation = Quaternion.LookRotation(-directionToCenter);  // Negative direction to face inward


            if (stone.GetComponent<Collider>() == null)
            {
                BoxCollider collider = stone.AddComponent<BoxCollider>();  // Adding a BoxCollider if it doesn't already exist
                collider.size = new Vector3(1.5f, 0.1f, 1.5f);
            }
        }
    }
}