import numpy as np, cv2, sys

TOP = 0.84
def v(*a): return np.array(a, float)

# Solid scenery from CafeSceneBuilder (centre, size) boxes; cylinders approximated by boxes.
BOXES = {
    "machine body": (v(0.05, TOP + 0.225, 0.84), v(0.62, 0.45, 0.34)),
    "group head":   (v(-0.05, TOP + 0.32, 0.6), v(0.095, 0.06, 0.095)),
    "gh neck":      (v(-0.05, TOP + 0.36, 0.635), v(0.1, 0.06, 0.07)),
    "grinder body": (v(-0.5, TOP + 0.18, 0.78), v(0.18, 0.36, 0.22)),
    "hopper":       (v(-0.5, TOP + 0.44, 0.78), v(0.13, 0.16, 0.13)),
    "drip tray":    (v(0.05, TOP + 0.0125, 0.585), v(0.58, 0.025, 0.17)),
    "steam pipe":   (v(0.25, TOP + 0.22, 0.6), v(0.024, 0.29, 0.024)),
}
FORK = v(-0.5, TOP + 0.13, 0.6)
GRIND_BTN = v(-0.44, TOP + 0.29, 0.66)
DISPLAY = v(-0.5, TOP + 0.345, 0.665)
BREW_BTN = v(-0.05, TOP + 0.41, 0.66)
GROUP_HEAD = v(-0.05, TOP + 0.32, 0.6)
GAUGE = v(0.05, TOP + 0.55, 0.75)
STEAM_REST = v(0.25, TOP + 0.025, 0.6)
STEAM_BTN = v(0.25, TOP + 0.41, 0.66)
WAND_TIP = v(0.25, TOP + 0.075, 0.6)
WAND_PTS = [WAND_TIP + v(0, h, 0) for h in (0, 0.14, 0.28)]
PF = v(-0.05, TOP + 0.28, 0.6)
HANDLE = [PF + v(0, -0.008, z) for z in (-0.11, -0.16, 0)]
BREW_SLOTS = [(0.55, 0.04), (0.64, 0.12), (0.5, -0.1)]
STEAM_SLOTS = [(0, 0.22), (0, -0.2), (0.14, 0.2), (-0.14, 0.2)]

READ, WIDTH = 0.62, 0.3
SLOTS = [(-0.3, 0.06), (-0.32, -0.06), (-0.34, 0.18), (-0.22, 0.2)]

def look(fwd):
    f = fwd / np.linalg.norm(fwd)
    r = np.cross([0, 1, 0], f); r /= np.linalg.norm(r)
    u = np.cross(f, r)
    return r, u, f

def ray_box(o, d, c, s):
    lo, hi = c - s / 2, c + s / 2
    t0, t1 = 0.0, 1.0
    for i in range(3):
        if abs(d[i]) < 1e-9:
            if o[i] < lo[i] or o[i] > hi[i]: return False
            continue
        a, b = (lo[i] - o[i]) / d[i], (hi[i] - o[i]) / d[i]
        a, b = min(a, b), max(a, b)
        t0, t1 = max(t0, a), min(t1, b)
        if t0 > t1: return False
    return True

class Cam:
    def __init__(self, eye, target, fov=60, w=960, h=540):
        self.eye, self.w, self.h = eye, w, h
        self.r, self.u, self.f = look(target - eye)
        self.k = (h / 2) / np.tan(np.radians(fov / 2))
    def vp(self, p):
        d = p - self.eye
        z = d @ self.f
        if z <= 1e-6: return None
        return np.array([0.5 + (d @ self.r) * self.k / z / self.w, 0.5 + (d @ self.u) * self.k / z / self.h, z])
    def px(self, p):
        q = self.vp(p)
        return None if q is None else (int(q[0] * self.w), int((1 - q[1]) * self.h))

def overlaps(cam, pos, R, hw, hh, point):
    c, s = cam.vp(pos), cam.vp(point)
    if c is None or s is None: return False
    e, t = cam.vp(pos + R[0] * hw), cam.vp(pos + R[1] * hh)
    return abs(s[0] - c[0]) < abs(e[0] - c[0]) and abs(s[1] - c[1]) < abs(t[1] - c[1])

def hits_disc(cam, pos, R, hw, hh, centre, rad):
    pts = [centre] + [centre + d * rad for d in (v(1,0,0), v(-1,0,0), v(0,1,0), v(0,-1,0))]
    return any(overlaps(cam, pos, R, hw, hh, p) for p in pts)

def covers(eye, pos, R, hw, hh, point):
    n = R[2]; ray = point - eye; den = ray @ n
    if den <= 1e-4: return False
    t = ((pos - eye) @ n) / den
    if t <= 0 or t >= 1: return False
    loc = eye + ray * t - pos
    return abs(loc @ R[0]) < hw and abs(loc @ R[1]) < hh

def blocked(eye, pos, R, hw, hh):
    n = 0
    for x in (-1, 0, 1):
        for y in (-1, 0, 1):
            p = pos + R[0] * x * hw + R[1] * y * hh
            if any(ray_box(eye, p - eye, c, s) for c, s in BOXES.values()): n += 1
    return n

def in_view(cam, p, m):
    q = cam.vp(p)
    return q is not None and m < q[0] < 1 - m and m < q[1] < 1 - m

def closer_cut(cam, pos, R, hw, hh, point, rad=0):
    """True when scenery is nearer than the card and lands on it, which is what slices the words."""
    pts = [point] if rad == 0 else [point] + [point + d * rad for d in (v(1, 0, 0), v(-1, 0, 0), v(0, 1, 0), v(0, -1, 0))]
    sign_z = cam.vp(pos)
    if sign_z is None:
        return True
    return any(overlaps(cam, pos, R, hw, hh, p) and (q := cam.vp(p)) is not None and q[2] < sign_z[2] - 0.01 for p in pts)

def place_slots(eye, target, slots, lines, steam=False, brew=False):
    to = target - eye
    dist = float(np.clip(min(READ, np.linalg.norm(to) * 0.8), 0.4, READ))
    if steam:
        dist = min(dist, 0.42)
    r, u, f = look(to)
    centre = eye + f * dist
    hh = (0.022 + lines * 0.022) / 2 + 0.015
    hw = WIDTH / 2 + 0.015
    cam = Cam(eye, target)
    best, bs = None, 1e9
    for sx, sy in slots:
        pos = centre + r * sx + u * sy
        R = look(pos - eye)
        s = 100 if covers(eye, pos, R, hw, hh, target) else 0
        if brew or steam:
            if hits_disc(cam, pos, R, hw, hh, GROUP_HEAD, 0.05):
                s += 100
            s += 30 * blocked(eye, pos, R, hw, hh)
        if brew:
            if any(overlaps(cam, pos, R, hw, hh, p) for p in HANDLE):
                s += 100
            if any(hits_disc(cam, pos, R, hw, hh, p, 0.02) for p in WAND_PTS):
                s += 100
        if not in_view(cam, pos, 0.04):
            s += 5
        if s < bs:
            bs, best = s, (pos, R, hw, hh, (sx, sy))
        if s == 0:
            break
    return best, bs

def place(eye, target, lines=6):
    to = target - eye
    dist = np.clip(min(READ, np.linalg.norm(to) * 0.8), 0.4, READ)
    r, u, f = look(to)
    centre = eye + f * dist
    hh = (0.022 + lines * 0.022) / 2 + 0.015
    hw = WIDTH / 2 + 0.015
    cam = Cam(eye, target)
    best, bs, log = None, 1e9, []
    for sx, sy in SLOTS:
        pos = centre + r * sx + u * sy
        R = look(pos - eye)
        s = 0
        if covers(eye, pos, R, hw, hh, target): s += 100
        if hits_disc(cam, pos, R, hw, hh, BREW_BTN, 0.03): s += 100
        if hits_disc(cam, pos, R, hw, hh, GROUP_HEAD, 0.05): s += 100
        s += 30 * blocked(eye, pos, R, hw, hh)
        for k in (DISPLAY, GRIND_BTN, FORK):
            if covers(eye, pos, R, hw, hh, k): s += 10
        if not in_view(cam, pos, 0.04): s += 5
        log.append(((sx, sy), s))
        if s < bs: bs, best = s, (pos, R, hw, hh)
        if s == 0: break
    return best, bs, log, cam

def render(name, eye, target, best, cam):
    img = np.full((cam.h, cam.w, 3), 70, np.uint8)
    for label, (c, s) in BOXES.items():
        corners = [c + s / 2 * v(a, b, d) for a in (-1, 1) for b in (-1, 1) for d in (-1, 1)]
        pts = [cam.px(p) for p in corners]
        if None in pts: continue
        hull = cv2.convexHull(np.array(pts, np.int32))
        cv2.fillConvexPoly(img, hull, (110, 110, 120) if "grinder" not in label and "hopper" not in label else (40, 40, 40))
    for p, col in ((FORK, (0, 200, 255)), (GRIND_BTN, (0, 180, 0)), (DISPLAY, (150, 255, 150)), (BREW_BTN, (0, 0, 160))):
        q = cam.px(p)
        if q: cv2.circle(img, q, 8, col, -1)
    pos, R, hw, hh = best
    quad = [cam.px(pos + R[0] * a * (hw - 0.015) + R[1] * b * (hh - 0.015)) for a, b in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
    cv2.fillConvexPoly(img, np.array(quad, np.int32), (225, 240, 248))
    cv2.putText(img, name, (10, 25), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
    return img

if __name__ == "__main__":
    eyes = {"seated": v(0, 1.2, 0), "slid left": v(-0.2, 1.2, 0), "slid right": v(0.2, 1.2, 0),
            "leaning in": v(-0.05, 1.15, 0.12), "right, leaning in": v(0.25, 1.2, 0.15)}
    tiles, fail = [], False
    for ename, eye in eyes.items():
        for tname, tgt in (("fork", FORK), ("grind button", GRIND_BTN)):
            best, score, log, _ = place(eye, tgt)
            pos, R, hw, hh = best
            # The sign stays put; check it from every head position, looking at the sign.
            for vname, view in eyes.items():
                cam = Cam(view, pos)
                bad = (blocked(view, pos, R, hw, hh) > 0
                       or hits_disc(cam, pos, R, hw, hh, GROUP_HEAD, 0.05)
                       or hits_disc(cam, pos, R, hw, hh, BREW_BTN, 0.03)
                       or covers(view, pos, R, hw, hh, tgt))
                fail |= bad
                if bad or vname == ename:
                    print(f"placed {ename:17s} -> {tname:12s} viewed {vname:17s} {'FAIL' if bad else 'ok'}")
                if vname == "right, leaning in" or bad:
                    tiles.append(render(f"{ename} -> {vname}: {'FAIL' if bad else 'ok'}", view, tgt, best, Cam(view, (pos + tgt) / 2)))
    cards = [
        ("brew running", GAUGE, BREW_SLOTS, 4, False, True),
        ("shot done / steam rest", STEAM_REST, STEAM_SLOTS, 2, True, False),
        ("shot done / steam button", STEAM_BTN, STEAM_SLOTS, 2, True, False),
    ]
    for ename, eye in eyes.items():
        for cname, tgt, slots, lines, steam, brew in cards:
            best, score = place_slots(eye, tgt, slots, lines, steam, brew)
            pos, R, hw, hh, slot = best
            for vname, view in eyes.items():
                cam = Cam(view, pos)
                if brew:
                    bad = any(closer_cut(cam, pos, R, hw, hh, p, 0.02) for p in WAND_PTS) or closer_cut(cam, pos, R, hw, hh, GROUP_HEAD, 0.05)
                else:
                    bad = closer_cut(cam, pos, R, hw, hh, GROUP_HEAD, 0.05) or blocked(view, pos, R, hw, hh) > 0
                fail |= bad
                if bad or vname == ename:
                    print(f"{cname:24s} placed {ename:17s} slot {slot} viewed {vname:17s} score {score:3d} {'FAIL' if bad else 'ok'}")
            tiles.append(render(f"{cname} / {ename}", eye, tgt, (pos, R, hw, hh), Cam(eye, (pos + tgt) / 2)))
    if len(tiles) % 2:
        tiles.append(np.zeros_like(tiles[0]))
    rows = [np.hstack(tiles[i:i + 2]) for i in range(0, len(tiles), 2)]
    sheet = cv2.resize(np.vstack(rows), None, fx=0.6, fy=0.6)
    cv2.imwrite(sys.argv[1] if len(sys.argv) > 1 else "sign_test.png", sheet)
    sys.exit(1 if fail else 0)
