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
			// Conditional variable declaration saves a small amount of time in this method.
			Vector3 direction;
			// Take the difference between the two points.
			direction =  destination - transform.position;
			// Normalize it.
			direction.Normalize();
			// Move toward it at a rate of speed per Time.deltaTime.
			controller.SimpleMove(direction * speed);
			// If we haven't moved (and are therefore stuck) reset the destination.
			if(controller.velocity==Vector3.zero)
			{
				destination = transform.position;
				animation.Play("idle");
			}
			else
			{
				animation.Play("walk");
			}
			// Set the direction to rotate toward.
			direction = Vector3.Slerp(transform.TransformPoint(Vector3.forward), destination, Time.deltaTime);
			// Mute the y-axis in the direction, as to prevent tilting.
			direction.y = transform.position.y;
			// Rotate slowly to look the destination
			transform.LookAt(direction);
		}
	}
}
