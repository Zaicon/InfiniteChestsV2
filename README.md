# InfiniteChestsV2

This is a completely rewritten version of MarioE's original InfiniteChests plugin. Most of this plugin was copied or modified from that one.

### Plugin Checklist:
- [x] Converting map chests to database chests upon server start.
- [ ] || via command.
- [x] Users can interact (add, modify, or destory) database chests.
- [x] Users can protect their chests via user account.
- [x] Users can protect their chests via password.
- [x] Users can set their chests to "public" (other players can freely modify).
- [x] Permissions for individual functions.
- [ ] Chests refill items (immediately or with a timer).
- [ ] "Quick Stack To Nearby Chests" supported.
- [ ] Converting database chests back to map chests.
- [ ] Pruning empty and/or corrupted chests.

### Things this plugin will not do (as of now):
* Chest name support. Chest names are stored in tile data, which would be very costly to implement database-chest names in tile data.
* "Bank" chests. In the future, I would like to have a way to save chests across maps. However, it is not currently a priority.

### Commands
```
/chest claim - Protects a chest via user account.
/chest unclaim - Unprotects a chest.
/chest info - Displays X/Y coordinates and account owner.
/chest password <password> - Password-protects a chest.
/chest unlock <password> - Allows opening of any chest using that password.
/chest public - Toggles the 'public' setting of a chest, allowing others to use but not destroy the chest.
/chest cancel - Cancels any of the above actions.
```

###Permisisons
```
ic.use - Enables use of /chest
ic.claim - Enables use of /chest claim, unclaim, password
ic.info - Enables use of /chest info
ic.public - Enables use of /chest public
ic.protect - Players with this permission will have their chests automatically protected via user account.
ic.edit - Allows player to edit any chest regardless of chest protection.
```
