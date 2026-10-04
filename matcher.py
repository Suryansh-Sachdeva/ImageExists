import sys
import cv2


def main():
    if len(sys.argv) != 3:
        print("Usage: matcher.py <screenshot> <template>")
        sys.exit(1)

    screenshot_path = sys.argv[1]
    template_path = sys.argv[2]

    # Read the screenshot and template image
    screenshot = cv2.imread(screenshot_path)
    template = cv2.imread(template_path)

    if screenshot is None:
        print("Could not read screenshot.")
        sys.exit(1)

    if template is None:
        print("Could not read template image.")
        sys.exit(1)

    # Make sure the template is not larger than the screenshot
    screenshot_height, screenshot_width = screenshot.shape[:2]
    template_height, template_width = template.shape[:2]

    if template_width > screenshot_width or template_height > screenshot_height:
        print("false")
        return

    # Perform template matching
    result = cv2.matchTemplate(
        screenshot,
        template,
        cv2.TM_CCOEFF_NORMED
    )

    # Find the best match
    min_val, max_val, min_loc, max_loc = cv2.minMaxLoc(result)

    # Matching threshold
    threshold = 0.80

    if max_val >= threshold:
        print("true")
    else:
        print("false")


if __name__ == "__main__":
    main()