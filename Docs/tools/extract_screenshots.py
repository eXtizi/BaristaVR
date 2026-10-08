"""Pulls the slide screenshots out of the recorded exe run into Docs/screenshots/."""
import os

import cv2

DOCS = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VIDEO = os.path.join(DOCS, "Recording 2026-10-08 123110.mp4")
OUT = os.path.join(DOCS, "screenshots")

# name: (seconds, crop box in 1920x1080 pixels or None)
SHOTS = {
    "cafe_overview": (16, None),
    "exe_launch": (9, None),
    "brew_gauge": (70, None),
    "pour": (102, None),
    "result_report": (113, (881, 178, 1378, 834)),
}

os.makedirs(OUT, exist_ok=True)
cap = cv2.VideoCapture(VIDEO)
for name, (t, box) in SHOTS.items():
    cap.set(cv2.CAP_PROP_POS_MSEC, t * 1000)
    ok, frame = cap.read()
    if not ok:
        raise SystemExit(f"could not read frame at {t}s")
    if box:
        x0, y0, x1, y1 = box
        frame = frame[y0:y1, x0:x1]
    cv2.imwrite(os.path.join(OUT, name + ".png"), frame)
    print("wrote", name)
