using UnityEngine;
using System.Collections;

/// <summary>
/// This component allows objects to be equipped by players, thereby attaching them to their armatures and allowing them to be 
/// used as weapons, armour, etc.
/// </summary>
public class Equipable : MonoBehaviour
{
	/// <summary>
	/// This is a string representing the object in the player hierarchy that this item should be attached to.
	/// </summary>
	public string attachTo;
	
	/// <summary>
	/// This is the Equip method, called from the clickController component, that allows objects to be equipped upon clicking or
	/// tapping. It equips by parenting the player to this object and nulling its position and rotation.
	/// </summary>
	/// <param name='player'>
	/// The player being attached to.
	/// </param>
	public void Equip (GameObject player) {
		transform.parent = player.transform.Find(attachTo);
		transform.localPosition = Vector3.zero;
		transform.localRotation = Quaternion.identity;
		gameObject.layer = 9;
	}
	
	/// <summary>
	/// This is the Unequip method, called from the clickController component, that allows objects to be unequipped upon clicking
	/// or tapping. It unequips by parenting the player to this object and nulling its position and rotation.
	/// </summary>
	/// <param name='player'>
	/// The player being attached to.
	/// </param>
	public void Unequip () {
		transform.parent = null;
		gameObject.layer = 0;
	}
}
