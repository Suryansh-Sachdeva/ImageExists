
import sys
import cv2
import numpy as np


def read_image(image_path):
    """
    Reads an image, including paths containing Unicode characters.
    Returns the image in grayscale.
    """

    try:
        image_data = np.fromfile(
            image_path,
            dtype=np.uint8
        )

        image = cv2.imdecode(
            image_data,
            cv2.IMREAD_GRAYSCALE
        )

        return image

    except Exception as error:
        print(
            f"Could not read image: {error}",
            file=sys.stderr
        )
        return None


def main():
    if len(sys.argv) != 3:
        print(
            "Usage: matcher.py <screenshot> <template>",
            file=sys.stderr
        )
        sys.exit(1)

    screenshot_path = sys.argv[1]
    template_path = sys.argv[2]

    # 1. Read both images in grayscale.

    screenshot = read_image(screenshot_path)
    template = read_image(template_path)

    if screenshot is None:
        print(
            "Could not read screenshot.",
            file=sys.stderr
        )
        sys.exit(1)

    if template is None:
        print(
            "Could not read template image.",
            file=sys.stderr
        )
        sys.exit(1)

    # 2. Check image dimensions.

    screenshot_height, screenshot_width = screenshot.shape
    template_height, template_width = template.shape

    if (template_width > screenshot_width or
            template_height > screenshot_height):
        print("false")
        return

    # 3. Perform template matching.

    result = cv2.matchTemplate(
        screenshot,
        template,
        cv2.TM_CCOEFF_NORMED
    )

    # 4. Find the best matching location.

    min_val, max_val, min_loc, max_loc = cv2.minMaxLoc(result)

    # 5. Compare against the matching threshold.

    threshold = 0.80

    if max_val >= threshold:
        print("true")
    else:
        print("false")


if __name__ == "__main__":
    main()
