using UnityEngine;
using System.Collections;

/// <summary>
/// This is the ClickController component. It is used to recognize clicks/taps on the environment by the user to allow for
/// mouse/finger based navigation similar to that in World of Warcraft. The user clicks on a location and the player object
/// moves toward it. To comply with the KeyController component, a third component, the WalkController, is used. When the player
/// needs to move, the destination is assigned to that component and it moves the player using standard Unity method calls.
/// </summary>
public class ClickController : MonoBehaviour
{
	/// <summary>
	/// This is a reference to the walk controller component used to speed method calls on this class at runtime by preventing
	/// the use of the GetComponent method at any point other than initialization. It is assigned in the Start method.
	/// </summary>
	private WalkController walkController;
	/// <summary>
	/// This is the RaycastHit detected from the last raycast.
	/// </summary>
    private RaycastHit hit;
	
	/// <summary>
	/// This is the Start method, called at the initialization of the class and used to set walkController to the instance of
	/// WalkController on this object.
	/// </summary>
	void Start ()
	{
		walkController = GetComponent<WalkController>();
	}
	
	/// <summary>
	/// This is the Update method, called every frame to test for clicks/taps, then progressively for raycast collisions and 
	/// assigns the the Walk Controller's destination.
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
