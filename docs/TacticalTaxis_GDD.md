# Tactical Taxis: Game Design Document

## Elevator Pitch
A high-octane blend of Crazy Taxi style arcade taxi racing and competitive PvP combat.

## Scoring
The primary objective is to complete the highest number of successful taxi runs within the match time limit. Combat serves as a means to disrupt opponents and protect your own progress (or catch up, if someone’s a little too good at the game).

## Gameplay Mechanics

### Objectives
Players must navigate the city to pick up and drop off customers while under fire from rival drivers.

### Pick-ups
- Available at designated pick-up zones.
- Duration: Process takes 5-10 seconds per passenger in the group.
- Competition: If multiple drivers are in the same zone, customers choose the vehicle with the least damage.
- Location: You won’t find any customers near the taxi depot. Some areas have more customers than others, and some will be more likely to have customers that want to travel farther.

### Customer Variables
Visible to players before selection:
- **Distance**: Classified as Far, Moderate, or Near.
- **Wealth**: Displayed as a payment range ($->$$$).
- **Max Acceptable Damage**: Comfort level with vehicle condition (usually tied to wealth); customers will refuse entry if the vehicle is too damaged.

### Customer Retention
Erratic driving or taking excessive damage (via collisions or weapons) triggers a "Bail-out System," causing passengers to exit the vehicle prematurely, making you lose the cash they would have paid you.

### Getting Paid
Revenue is earned only upon successful delivery. Payouts are calculated based on:
- **Group Size**: Higher yields via a 1x multiplier per passenger.
- **Distance**: Longer routes command significantly higher fares but entail increased travel risk.

### Vehicle Health
Critical damage results in a taxi explosion. Destroyed players respawn at a central taxi depot, distant from prime pickup locations.

### Weapons & Loadouts
Weapons are acquired via Mario-Kart style pick-ups. Spawning is random, but weapon types are identifiable before collection.
- Location: You’ll find more weapons the further you are away from the taxi depot.

### Economy
Ammo reloads and vehicle repairs cost currency earned from successful runs. Better equipment incurs higher costs.

### Durability
Weapons have limited reloads before they are depleted and discarded.

### Classes
Players can carry one weapon per class at a time, with the ability to swap or drop items. Example: you can’t have more than one rocket launcher, but you can have a rocket launcher and an assault rifle.

### Combat Controls
Aiming is restricted to only being able to fire out the specific angles available from the driver’s side window to prevent self-damage, and to add challenge. A 3rd-person camera tracks the vehicle, while aiming lines and blast radius spheres provide visual feedback for targeting.

## Core Systems
- Customer Pickup/Drop-off Logic
- Dynamic Customer Bail-out System
- Multi-source Vehicle Damage (Environmental & Combat)
- Class-based Weapon Management
- Randomized Weapon Spawning & Acquisition
- Economy-based Repair Shop System

## Additional Sections (Future)
- Match Flow & Timer
- Win Conditions
- Maps / City Layout & Zone Design
- UI / HUD Requirements
- Audio & VFX
- Controls Diagram
- Art Direction
