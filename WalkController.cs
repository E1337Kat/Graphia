using UnityEngine;
using System.Collections;

public class WalkController : MonoBehaviour {
	/// <summary>
	/// A reference to the controller to speed runtime.
	/// </summary>
	private CharacterController controller;
	/// <summary>
	/// The previous position of the transform, used to detect if the object has become stuck.
	/// </summary>
	private Vector3 previousPosition;
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
	void Start () {
		controller = GetComponent<CharacterController>();
		destination = transform.position;
		previousPosition = transform.position;
	}
	
	/// <summary>
	/// If the object is not at it's destination, move toward it. If it can no longer move, reset it's destination. If we're not grounded, fall.
	/// </summary>
	void Update () {
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
			if(previousPosition==transform.position)
			{
				destination = transform.position;
				animation.Play("idle");
			}
			else
			{
				direction.y = transform.position.y;
				transform.LookAt(direction);
				animation.Play("walk");
			}
		}
	}
}
