import sys

try:
    import cv2
    import numpy as np

except (ImportError, OSError) as error:
    print(
        "DEPENDENCY_ERROR: A required Python package is missing or could not be loaded: "
        + str(error)
        + ". Install the required packages using: py -3 -m pip install opencv-python numpy",
        flush=True
    )

    sys.exit(1)


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
            file=sys.stderr,
            flush=True
        )
        return None


def match_images(screenshot_path, template_path):

    # 1. Read both images in grayscale.

    screenshot = read_image(screenshot_path)
    template = read_image(template_path)

    if screenshot is None:
        raise ValueError("Could not read screenshot.")

    if template is None:
        raise ValueError("Could not read template image.")

    # 2. Check image dimensions.

    screenshot_height, screenshot_width = screenshot.shape
    template_height, template_width = template.shape

    if (template_width > screenshot_width or
            template_height > screenshot_height):
        return False

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

    return max_val >= threshold


def main():

    print("READY", flush=True)
    print("Python matcher ready.", file=sys.stderr, flush=True)

    # Keep waiting for requests from C#.

    while True:

        # Read the screenshot path.

        screenshot_path = sys.stdin.readline()

        if not screenshot_path:
            break

        screenshot_path = screenshot_path.rstrip("\r\n")

        # Read the template path.

        template_path = sys.stdin.readline()

        if not template_path:
            break

        template_path = template_path.rstrip("\r\n")

        try:
            found = match_images(
                screenshot_path,
                template_path
            )

            if found:
                print("true", flush=True)
            else:
                print("false", flush=True)

        except Exception as error:

            print(
                f"Matching error: {error}",
                file=sys.stderr,
                flush=True
            )

            print("ERROR", flush=True)


if __name__ == "__main__":
    main()