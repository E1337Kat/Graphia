using UnityEngine;
using System.Collections;

/// <summary>
/// This is the KeyController component. It is used to detect key presses or joystick axis movement and move the player
/// accordingly. This movement is performed relative to the camera, so forward is always away from the camera, backwards
/// toward it, left to camera left, and right to camera right. To comply with the ClickController component, a third 
/// component, the WalkController, is used. When the a key is pressed, a destination Time.deltaTime distance from the player
/// is assigned to WalkController.
/// </summary>
public class KeyController : MonoBehaviour
{
	/// <summary>
	/// This is a reference to the walk controller component used to speed method calls on this class at runtime by preventing
	/// the use of the GetComponent method at any point other than initialization. It is assigned in the Start method.
	/// </summary>
	private WalkController walkController;
	
	/// <summary>
	/// This is the Start method, called at the initialization of the class and used to set walkController to the instance of
	/// WalkController on this object.
	/// </summary>
	void Start ()
	{
		walkController = GetComponent<WalkController>();
	}
	
	/// <summary>
	/// This is the Update method. It is called every frame to test if either of the directional axises are being pressed. If
	/// so, it assigns an appropriate value to the WalkController destination to move the player accordingly.
	/// </summary>
	void Update ()
	{
		// If any of the arrow keys are being pressed,
		if(Input.GetButton("Horizontal") || Input.GetButton("Vertical"))
		{
			// Calculate a vector of the direction to move in.
			Vector3 direction = Vector3.right * Input.GetAxis("Horizontal");
			direction += Vector3.forward * Input.GetAxis("Vertical");
			
			// Normalize and multiply by deltaTime to remove any bias in player movement.
			direction.Normalize();
			direction *= Time.deltaTime;
			
			// Compensate for offset on camera position.
			direction += Camera.main.transform.InverseTransformPoint(transform.position);
			
			// Transform the camera destination into world space.
			direction = Camera.main.transform.TransformPoint(direction);
			
			// Assign the target to the walk controller's destination to begin movement.
			walkController.destination = direction;
		}
	}
}
