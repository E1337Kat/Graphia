using UnityEngine;
using System.Collections;

public class ClickController : MonoBehaviour
{
	/// <summary>
	/// A reference to the NavMeshAgent to speed runtime.
	/// </summary>
	private WalkController walkController;
	/// <summary>
	/// The hit from last raycast thrown to detect objects from the mouse position.
	/// </summary>
    private RaycastHit hit;
	
	/// <summary>
	/// Initialize walkController to the instance of NavMeshAgent on this object.
	/// </summary>
	void Start ()
	{
		walkController = GetComponent<WalkController>();
	}
	
	/// <summary>
	/// Every frame, if the click button (as defined in the input manager) is being held, calculate the first raycast hit from the mouse position and set the player's NavMeshAgent to move toward it.
	/// </summary>
	void Update ()
	{
		// If the click button is being held. This button is defined as an axis as to be portable to different platforms.
		if (Input.GetButton("Click"))
		{
			// Calculate the raycast from the mouse position and update the hit variable.
			if (Physics.Raycast(Camera.main.ScreenPointToRay(Input.mousePosition), out hit))
			{
				// Set the destination of the NavMeshAgent to the raycast point.
				walkController.destination = hit.point;
			}
		}
	}
}
