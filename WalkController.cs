using UnityEngine;
using System.Collections;

public class WalkController : MonoBehaviour
{
	/// <summary>
	/// A reference to the controller to speed runtime.
	/// </summary>
	private CharacterController controller;
	/// <summary>
	/// The speed at which to move.
	/// </summary>
	public float speed;
	/// <summary>
	/// The destination to move toward.
	/// </summary>
	public Vector3 destination;
	
	/// <summary>
	/// Initialize the destination to the object's position and the previous position to the current position.
	/// </summary>
	void Start ()
	{
		controller = GetComponent<CharacterController>();
		destination = transform.position;
	}
	
	/// <summary>
	/// If the object is not at it's destination, move toward it. If it can no longer move, reset it's destination. If we're not grounded, fall.
	/// </summary>
	void Update ()
	{
		// If we are not yet at our destination:
		if(destination!=transform.position)
		{
			// Find forward relative to the player.
			Vector3 direction = transform.forward;
			// Interpolate between forward and the destination.
			direction = Vector3.Slerp(direction, destination, Time.deltaTime);
			// Mute the y-axis in the direction, as to prevent tilting.
			direction.y = transform.position.y;
			// Rotate slowly to look in the correct direction.
			transform.LookAt(direction);
			
			// Move forward.
			controller.SimpleMove(transform.forward * speed);
		}
		// If we haven't moved (and are therefore stuck):
		if(controller.velocity==Vector3.zero)
		{
			// Reset the destination, and
			destination = transform.position;
			// Switch to the idle animation.
			animation.Play("idle");
		}
		else
		{
			// Otherwise, use the walk animation.
			animation.Play("walk");
		}
	}
}
